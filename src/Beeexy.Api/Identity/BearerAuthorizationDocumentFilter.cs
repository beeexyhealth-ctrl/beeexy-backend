using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Beeexy.Api.Identity;

internal sealed class BearerAuthorizationDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        foreach (var apiDescription in context.ApiDescriptions)
        {
            var metadata = apiDescription.ActionDescriptor.EndpointMetadata;
            if (!metadata.OfType<IAuthorizeData>().Any() ||
                metadata.OfType<IAllowAnonymous>().Any() ||
                apiDescription.HttpMethod is null ||
                apiDescription.RelativePath is null)
            {
                continue;
            }

            var path = $"/{apiDescription.RelativePath.Split('?')[0]}";
            if (!document.Paths.TryGetValue(path, out var pathItem) ||
                pathItem.Operations is null ||
                !pathItem.Operations.TryGetValue(
                    new HttpMethod(apiDescription.HttpMethod),
                    out var operation))
            {
                continue;
            }

            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
                }
            ];
        }
    }
}
