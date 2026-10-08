using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Authentication.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only: models the TCP peer of each request. It runs before every other middleware and sets the
/// connecting address from <c>X-Test-Connection-Address</c> (default 192.0.2.10), so the real forwarded-header
/// and rate-limit pipeline can be exercised in <c>TestServer</c> exactly as in production.
/// </summary>
public sealed class TestConnectionAddressStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Connection-Address";
    public const string DefaultAddress = "192.0.2.10";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, pipeline) =>
        {
            var requested = context.Request.Headers[HeaderName].ToString();
            context.Connection.RemoteIpAddress = IPAddress.Parse(string.IsNullOrWhiteSpace(requested) ? DefaultAddress : requested);
            await pipeline();
        });

        next(app);
    };
}
