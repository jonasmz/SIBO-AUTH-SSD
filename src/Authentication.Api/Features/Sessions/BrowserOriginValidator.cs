using Authentication.Infrastructure.Sessions;
using Microsoft.Extensions.Options;

namespace Authentication.Api.Features.Sessions;

public sealed class BrowserOriginValidator(IOptions<RefreshSessionOptions> options)
{
    // Startup validation accepts any absolute origin URI (trailing slash, host case, default port),
    // so both sides are compared in the same canonical scheme://host[:port] form.
    private readonly string _configuredOrigin = new Uri(options.Value.FrontendOrigin, UriKind.Absolute).GetLeftPart(UriPartial.Authority);

    public bool IsTrusted(HttpRequest request)
    {
        var origin = request.Headers.Origin;
        return origin.Count == 1 &&
            Uri.TryCreate(origin[0], UriKind.Absolute, out var parsed) &&
            string.Equals(parsed.GetLeftPart(UriPartial.Authority), _configuredOrigin, StringComparison.Ordinal);
    }
}
