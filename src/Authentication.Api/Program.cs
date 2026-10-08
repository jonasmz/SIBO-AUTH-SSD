using Authentication.Api.Features.Health;
using Authentication.Api.Features.Login;
using Authentication.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddAuthenticationInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

await app.Services.InitializeAuthenticationInfrastructureAsync();

app.MapHealthEndpoints();
app.MapLoginEndpoint();

await app.RunAsync();

public partial class Program
{
}
