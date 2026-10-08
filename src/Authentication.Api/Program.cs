using Authentication.Api.Features.Administration;
using Authentication.Api.Features.Health;
using Authentication.Api.Features.Login;
using Authentication.Api.Features.PasswordRecovery;
using Authentication.Api.Features.Passwords;
using Authentication.Infrastructure;
using Authentication.Api.Features.Sessions;
using Authentication.Api.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddAuthenticationInfrastructure(builder.Configuration);
builder.Services.AddSingleton<RefreshCookieWriter>();
builder.Services.AddSingleton<BrowserOriginValidator>();
builder.Services.AddTrustedForwardedHeaders(builder.Configuration);
builder.Services.AddAuthenticationRateLimiting(builder.Configuration);
builder.Services.AddSingleton<RecoveryAddressLimiter>();

var app = builder.Build();

// First, so every later middleware, limit, and log sees the effective client address, never a forged one.
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseRateLimiter();
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
