using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Authentication.Api.Documentation;

/// <summary>
/// Request records expose computed, get-only helpers (<c>IsValid</c>, <c>TrimmedName</c>) that clients never send.
/// They are removed from the request schemas so the contract lists only the accepted fields; the records and
/// their deserialization are untouched.
/// </summary>
internal sealed class RequestSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        var type = context.JsonTypeInfo;
        if (!type.Type.Name.EndsWith("Request", StringComparison.Ordinal) || schema.Properties is null)
        {
            return Task.CompletedTask;
        }

        foreach (var computed in type.Properties.Where(property => property.Set is null))
        {
            schema.Properties.Remove(computed.Name);
            schema.Required?.Remove(computed.Name);
        }

        return Task.CompletedTask;
    }
}
