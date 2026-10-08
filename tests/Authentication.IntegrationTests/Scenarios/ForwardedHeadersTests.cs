using System.Net;
using System.Text;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class ForwardedHeadersTests
{
    private const string Peer = "192.0.2.10";

    [Fact]
    public async Task WithoutAuthorizedProxiesAForgedForwardedHeaderChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = Factory(new Dictionary<string, string>());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Rotating the forged value does not hand out fresh allowance: the real peer is always the partition.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, Peer, "198.51.100.1", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, Peer, "198.51.100.2", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, Peer, "198.51.100.3", cancellationToken)).StatusCode);

        // With nothing authorized the processing is off: the framework would otherwise trust every sender
        // when both lists are empty.
        var forwarded = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(ForwardedHeaders.None, forwarded.ForwardedHeaders);
        Assert.Empty(forwarded.KnownProxies);
        Assert.Empty(forwarded.KnownIPNetworks);

        // The event records the real peer, not the forged address.
        var limited = Assert.Single(factory.CapturedLogs, log => log.StartsWith("RateLimitApplied", StringComparison.Ordinal));
        Assert.Contains(Peer, limited, StringComparison.Ordinal);
        Assert.DoesNotContain("198.51.100", limited, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAuthorizedProxyOrNetworkMakesTheForwardedClientTheOriginAndOnlyUpToTheFirstUntrustedHop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var byAddress = Factory(new Dictionary<string, string> { ["ReverseProxy__TrustedProxies"] = Peer });
        using var addressClient = byAddress.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Through the authorized proxy each forwarded client has its own allowance.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(addressClient, Peer, "198.51.100.1", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(addressClient, Peer, "198.51.100.1", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(addressClient, Peer, "198.51.100.1", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(addressClient, Peer, "198.51.100.2", cancellationToken)).StatusCode);

        // A chain is read from the right and stops at the first hop that is not authorized: whatever a client
        // wrote before it (the leftmost value) cannot choose the partition.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(addressClient, Peer, "203.0.113.1, 198.51.100.7", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(addressClient, Peer, "203.0.113.2, 198.51.100.7", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(addressClient, Peer, "203.0.113.3, 198.51.100.7", cancellationToken)).StatusCode);

        // The same peer outside the authorized set (another source address) is not trusted.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(addressClient, "192.0.2.99", "198.51.100.1", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(addressClient, "192.0.2.99", "198.51.100.2", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(addressClient, "192.0.2.99", "198.51.100.3", cancellationToken)).StatusCode);

        // Only the two address headers are ever consulted, never the forwarded host.
        var configured = byAddress.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, configured.ForwardedHeaders);

        // An authorized network works like an authorized address.
        using var byNetwork = Factory(new Dictionary<string, string> { ["ReverseProxy__TrustedNetworks"] = "192.0.2.0/24" });
        using var networkClient = byNetwork.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(networkClient, "192.0.2.77", "198.51.100.1", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(networkClient, "192.0.2.77", "198.51.100.1", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(networkClient, "192.0.2.77", "198.51.100.1", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(networkClient, "192.0.2.77", "198.51.100.2", cancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("ReverseProxy__TrustedProxies", "not-an-address", "ReverseProxy:TrustedProxies")]
    [InlineData("ReverseProxy__TrustedNetworks", "10.0.0.0/99", "ReverseProxy:TrustedNetworks")]
    public void AnUnparsableEntryStopsStartupNamingOnlyTheSetting(string key, string value, string expectedSetting)
    {
        using var factory = Factory(new Dictionary<string, string> { [key] = value });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var message = string.Join(" | ", Chain(failure).Select(exception => exception.Message));

        Assert.Contains(expectedSetting, message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, message.Replace(expectedSetting, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    private static AuthenticationApiFactory Factory(Dictionary<string, string> settings)
    {
        settings["RateLimiting__Login__PermitLimit"] = "2";

        return new AuthenticationApiFactory(additionalSettings: settings);
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            yield return current;
            if (current.InnerException is null)
            {
                yield break;
            }
        }
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string peer, string forwardedFor, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent("""{"email":"nobody@example.test","password":"Wr0ng-Guess!"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add(TestConnectionAddressStartupFilter.HeaderName, peer);
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        return client.SendAsync(request, cancellationToken);
    }
}
