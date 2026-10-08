using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Authentication.Api.Documentation;

/// <summary>Describes the <c>application/json</c> request body of endpoints carrying <see cref="JsonRequestBodyMetadata"/>.</summary>
internal sealed class JsonRequestBodyTransformer : IOpenApiOperationTransformer
{
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var body = context.Description.ActionDescriptor.EndpointMetadata.OfType<JsonRequestBodyMetadata>().FirstOrDefault();
        if (body is null || context.Document is null)
        {
            return;
        }

        var schemaName = body.BodyType.Name;
        var schema = await context.GetOrCreateSchemaAsync(body.BodyType, null, cancellationToken).ConfigureAwait(false);
        context.Document.AddComponent(schemaName, schema);

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = new OpenApiSchemaReference(schemaName, context.Document) }
            }
        };
    }
}
