/*
 * BearerSecurityDocumentTransformer.cs
 * -----------------------------------------------------------------------------
 * Purpose : Declares the JWT bearer scheme used by authenticated API operations
 *           in the generated OpenAPI document.
 * -----------------------------------------------------------------------------
 */

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SolarMicrogrid.API.OpenApi;

public sealed class BearerSecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Describe bearer input without embedding a token, credential, or environment-specific URL.
        document.Info.Title = "SolarGrid Exchange API";
        document.Info.Description =
            "Authenticated SolarGrid API. Use a short-lived JWT in the Authorization header.";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Enter the JWT only; do not paste the word Bearer into generated clients."
        };

        return Task.CompletedTask;
    }
}
