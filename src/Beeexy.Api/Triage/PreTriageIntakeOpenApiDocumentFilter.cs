using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Beeexy.Api.Triage;

internal sealed class PreTriageIntakeOpenApiDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        if (!document.Paths.TryGetValue(
                "/api/v1/pre-triage/intake",
                out var path) ||
            path.Operations is null ||
            !path.Operations.TryGetValue(new HttpMethod("POST"), out var operation))
        {
            return;
        }

        var idempotencyKey = operation.Parameters?.SingleOrDefault(parameter =>
            string.Equals(
                parameter.Name,
                PreTriageEndpointExtensions.IdempotencyKeyHeader,
                StringComparison.OrdinalIgnoreCase));
        if (idempotencyKey is not OpenApiParameter mutableParameter)
        {
            throw new InvalidOperationException(
                "The intake Idempotency-Key OpenAPI parameter is missing.");
        }

        mutableParameter.Required = true;
        mutableParameter.Description =
            "Required URL-safe opaque key (1-128 characters). Reuse it only when retrying " +
            "the same logical intake. The same scoped key with different text returns 409.";
    }
}
