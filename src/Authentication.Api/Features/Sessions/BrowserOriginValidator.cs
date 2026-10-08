using Authentication.Infrastructure.Sessions;
using Microsoft.Extensions.Options;

namespace Authentication.Api.Features.Sessions;

public sealed class BrowserOriginValidator(IOptions<RefreshSessionOptions> options)
{
    public bool IsTrusted(HttpRequest request)
    {
        var origin = request.Headers.Origin;
        return origin.Count == 1 &&
            Uri.TryCreate(origin[0], UriKind.Absolute, out var parsed) &&
            string.Equals(parsed.GetLeftPart(UriPartial.Authority), options.Value.FrontendOrigin, StringComparison.Ordinal);
    }
}
