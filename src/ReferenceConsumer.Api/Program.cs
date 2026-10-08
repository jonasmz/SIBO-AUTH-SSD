using ReferenceConsumer.Api.Features.Health;
using ReferenceConsumer.Api.Security;

namespace ReferenceConsumer.Api;

// An explicit, namespaced entry point avoids a second global `Program` type that would be
// ambiguous with Authentication.Api's `Program` in the integration test project.
internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddProblemDetails();
        builder.Services.AddConsumerJwtValidation(builder.Configuration);

        var app = builder.Build();

        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapLivenessEndpoint();

        await app.RunAsync();
    }
}
