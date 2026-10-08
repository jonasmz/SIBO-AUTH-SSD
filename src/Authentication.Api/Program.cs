using Authentication.Api.Features.Administration;
using Authentication.Api.Features.Health;
using Authentication.Api.Features.Login;
using Authentication.Api.Features.PasswordRecovery;
using Authentication.Api.Features.Passwords;
using Authentication.Infrastructure;
using Authentication.Api.Features.Sessions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddAuthenticationInfrastructure(builder.Configuration);
builder.Services.AddSingleton<RefreshCookieWriter>();
builder.Services.AddSingleton<BrowserOriginValidator>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

await app.Services.InitializeAuthenticationInfrastructureAsync();

app.MapHealthEndpoints();
app.MapLoginEndpoint();
app.MapRefreshEndpoint();
app.MapLogoutEndpoint();
app.MapChangePasswordEndpoint();
app.MapForgotPasswordEndpoint();
app.MapResetPasswordEndpoint();
app.MapAdministrationEndpoints();

await app.RunAsync();

public partial class Program
{
}
