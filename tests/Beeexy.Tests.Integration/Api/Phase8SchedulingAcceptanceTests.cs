using System.Net;
using System.Text.Json;
using Beeexy.Tests.Integration.Support;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase8Acceptance")]
public sealed class Phase8SchedulingAcceptanceTests(PostgreSqlContainerFixture postgres)
{
    private static readonly string[] HttpMethods =
        ["delete", "get", "head", "options", "patch", "post", "put"];

    [Fact]
    public async Task OpenApi_ContainsExactEightOperationMatrixAndNoInternalSchedulingFields()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var expected = new[]
        {
            Operation("/api/v1/doctors/{doctorId}/slots", "get", false,
                "200", "404", "422", "500"),
            Operation("/api/v1/appointments", "post", true,
                "200", "201", "400", "401", "404", "409", "422", "500"),
            Operation("/api/v1/appointments", "get", true,
                "200", "400", "401", "404", "422", "500"),
            Operation("/api/v1/appointments/{id}", "get", true,
                "200", "401", "404", "500"),
            Operation("/api/v1/appointments/{id}/confirm", "post", true,
                "200", "401", "403", "404", "409", "500"),
            Operation("/api/v1/appointments/{id}/reject", "post", true,
                "200", "401", "403", "404", "409", "500"),
            Operation("/api/v1/appointments/{id}/cancel", "post", true,
                "200", "401", "404", "409", "500"),
            Operation("/api/v1/appointments/{id}/reschedule", "post", true,
                "200", "400", "401", "404", "409", "422", "500")
        };

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(46, paths.EnumerateObject().Count());
        Assert.Equal(
            expected.Select(value => $"{value.Method} {value.Path}").Order(),
            SchedulingOperations(paths).Order());
        foreach (var item in expected)
        {
            var operation = paths.GetProperty(item.Path).GetProperty(item.Method);
            Assert.Equal(item.Authenticated, operation.TryGetProperty("security", out _));
            Assert.Equal(
                item.Responses.Order(),
                operation.GetProperty("responses").EnumerateObject()
                    .Select(value => value.Name).Order());
        }

        var schemas = root.GetProperty("components").GetProperty("schemas");
        Assert.Equal(
            ["idempotencyKey", "modality", "patientId", "reason", "slotId"],
            Properties(schemas, "RequestAppointmentRequest").Order());
        Assert.Equal(
            ["slotId"],
            Properties(schemas, "RescheduleAppointmentRequest"));
        foreach (var schema in schemas.EnumerateObject().Where(value =>
            value.Name.Contains("Appointment", StringComparison.Ordinal) &&
            value.Name.EndsWith("Response", StringComparison.Ordinal)))
        {
            var serialized = schema.Value.ToString();
            foreach (var forbidden in new[]
            {
                "actorAccountId", "fingerprint", "idempotencyKey", "requestingAccountId",
                "version", "preTriage", "clinicalHistory", "diagnosis", "urgency", "fhir"
            })
            {
                Assert.DoesNotContain(forbidden, serialized, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static OperationExpectation Operation(
        string path,
        string method,
        bool authenticated,
        params string[] responses) =>
        new(path, method, authenticated, responses);

    private static IEnumerable<string> SchedulingOperations(JsonElement paths) =>
        paths.EnumerateObject()
            .Where(path =>
                path.Name == "/api/v1/doctors/{doctorId}/slots" ||
                path.Name.StartsWith("/api/v1/appointments", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => HttpMethods.Contains(operation.Name, StringComparer.Ordinal))
                .Select(operation => $"{operation.Name} {path.Name}"));

    private static IEnumerable<string> Properties(JsonElement schemas, string schemaName) =>
        schemas.GetProperty(schemaName).GetProperty("properties")
            .EnumerateObject().Select(value => value.Name);

    private sealed record OperationExpectation(
        string Path,
        string Method,
        bool Authenticated,
        string[] Responses);
}
