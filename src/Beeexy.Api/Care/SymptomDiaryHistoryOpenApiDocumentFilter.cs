using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Beeexy.Api.Care;

internal sealed class SymptomDiaryHistoryOpenApiDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        if (!document.Paths.TryGetValue(
                "/api/v1/pre-triage/episodes/{episodeId}/check-ins",
                out var path) ||
            path.Operations is null ||
            !path.Operations.TryGetValue(new HttpMethod("GET"), out var operation))
        {
            return;
        }

        var cursor = MutableParameter(operation, "cursor");
        cursor.Description =
            "Opaque continuation cursor returned by the preceding page. It is bound to " +
            "this episode and the normalized page size.";

        var pageSize = MutableParameter(operation, "pageSize");
        pageSize.Description = "Optional page size from 1 through 100; defaults to 20.";
        pageSize.Schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Integer,
            Format = "int32"
        };
    }

    private static OpenApiParameter MutableParameter(
        OpenApiOperation operation,
        string name)
    {
        var parameter = operation.Parameters?.SingleOrDefault(value =>
            string.Equals(value.Name, name, StringComparison.Ordinal));
        return parameter as OpenApiParameter ??
            throw new InvalidOperationException(
                $"The symptom-diary history {name} OpenAPI parameter is missing.");
    }
}
