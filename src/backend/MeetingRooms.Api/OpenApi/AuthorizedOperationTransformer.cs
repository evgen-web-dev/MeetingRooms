using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MeetingRooms.Api.OpenApi;

/// <summary>
/// Marks the operations that actually require a token, by reading the endpoint's authorization
/// metadata rather than by listing routes.
/// <para>
/// Per operation, not once for the whole document: a document-level requirement would claim
/// that register and login need a token, which is the opposite of true and the first thing a
/// reviewer would trip over.
/// </para>
/// </summary>
internal sealed class AuthorizedOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;

        // [AllowAnonymous] wins wherever it appears, exactly as it does at runtime.
        var requiresAuthorization =
            endpointMetadata.OfType<IAuthorizeData>().Any()
            && !endpointMetadata.OfType<IAllowAnonymous>().Any();

        if (!requiresAuthorization)
        {
            return Task.CompletedTask;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSecuritySchemeTransformer.SchemeName, context.Document)] = []
            }
        ];

        return Task.CompletedTask;
    }
}
