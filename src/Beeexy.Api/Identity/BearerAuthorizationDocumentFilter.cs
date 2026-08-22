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
            var optionalBearer = metadata
                .OfType<OptionalBearerAuthorizationMetadata>()
                .Any();
            var requiredBearer = metadata.OfType<IAuthorizeData>().Any() &&
                !metadata.OfType<IAllowAnonymous>().Any();
            if ((!requiredBearer && !optionalBearer) ||
                apiDescription.HttpMethod is null ||
                apiDescription.RelativePath is null)
            {
                continue;
            }

            var path = NormalizePath(apiDescription.RelativePath);
            if (!document.Paths.TryGetValue(path, out var pathItem) ||
                pathItem.Operations is null ||
                !pathItem.Operations.TryGetValue(
                    new HttpMethod(apiDescription.HttpMethod),
                    out var operation))
            {
                continue;
            }

            var bearerRequirement = new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
            };
            operation.Security = optionalBearer
                ? [new OpenApiSecurityRequirement(), bearerRequirement]
                : [bearerRequirement];
        }
    }

    private static string NormalizePath(string relativePath)
    {
        var segments = relativePath.Split('?')[0].Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (!segment.StartsWith('{') || !segment.EndsWith('}'))
            {
                continue;
            }

            var constraintIndex = segment.IndexOf(':');
            if (constraintIndex > 0)
            {
                segments[index] = $"{segment[..constraintIndex]}}}";
            }
        }

        return $"/{string.Join('/', segments)}";
    }
}

internal sealed class OptionalBearerAuthorizationMetadata;
