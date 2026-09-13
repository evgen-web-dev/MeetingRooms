using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MeetingRooms.Api.OpenApi;

/// <summary>
/// Declares the bearer scheme in the OpenAPI document, which is what gives Scalar an Authorize
/// control to paste a token into.
/// <para>
/// Convenience rather than capability: a bearer token can always be sent by typing an
/// Authorization header by hand. What the declaration buys is a reviewer on the deployed URL
/// pasting a token once instead of per request, and a document that records which endpoints
/// need one.
/// </para>
/// </summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    /// <summary>The component name, referenced by every operation that requires a token.</summary>
    public const string SchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.AddComponent(SchemeName, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the accessToken returned by POST /api/auth/login."
        });

        return Task.CompletedTask;
    }
}
