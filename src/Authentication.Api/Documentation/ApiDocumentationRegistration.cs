using Scalar.AspNetCore;

namespace Authentication.Api.Documentation;

/// <summary>
/// OpenAPI 3.1 contract (<c>Microsoft.AspNetCore.OpenApi</c>) and its read-only Scalar viewer (Technical
/// Constraints §12–13). Both are mapped only in Development; Production maps neither route.
/// </summary>
public static class ApiDocumentationRegistration
{
    public const string DocumentName = "v1";

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<JsonRequestBodyTransformer>();
            options.AddSchemaTransformer<RequestSchemaTransformer>();
        });

        return services;
    }

    /// <summary>Documents the JSON body an endpoint reads itself, without adding content-type enforcement.</summary>
    public static RouteHandlerBuilder DocumentsJsonRequest<TRequest>(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new JsonRequestBodyMetadata(typeof(TRequest)));
    }

    public static WebApplication MapApiDocumentationInDevelopment(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            // Read-only: navigate and read contracts, never send requests or keep credentials.
            options.HideTestRequestButton = true;
            options.HideClientButton = true;
            options.PersistentAuthentication = false;
            options.Telemetry = false;
        });

        return app;
    }
}
