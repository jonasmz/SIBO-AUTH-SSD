namespace Authentication.Api.Features.Sessions;

public sealed class RefreshCookieWriter(IHostEnvironment environment)
{
    public void Write(HttpResponse response, string credential, DateTimeOffset expiresAtUtc) => response.Cookies.Append("auth_refresh", credential, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/api/auth", Expires = expiresAtUtc, Secure = environment.IsProduction() });

    public void Clear(HttpResponse response) => response.Cookies.Append("auth_refresh", string.Empty, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/api/auth", Expires = DateTimeOffset.UnixEpoch, MaxAge = TimeSpan.Zero, Secure = environment.IsProduction() });
}
