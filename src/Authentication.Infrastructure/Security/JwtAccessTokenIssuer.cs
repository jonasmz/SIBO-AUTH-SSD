using System.Security.Claims;
using System.Security.Cryptography;
using Authentication.Application.Features.Login;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Authentication.Infrastructure.Security;

public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer, IDisposable
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly RSA _rsa;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _rsa = LoadKey(_options.PrivateKeyPath);
        _signingCredentials = new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256);
    }

    public AccessToken Issue(AuthenticatedIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var now = _timeProvider.GetUtcNow();
        var issuedAt = new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, identity.UserId),
            new(JwtRegisteredClaimNames.Email, identity.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange(identity.Roles.Select(role => new Claim("role", role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _signingCredentials
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    private static RSA LoadKey(string path)
    {
        var rsa = RSA.Create();

        try
        {
            rsa.ImportFromPem(File.ReadAllText(path));
            return rsa;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException or IOException
            or UnauthorizedAccessException)
        {
            rsa.Dispose();
            throw new InvalidOperationException("The JWT signing key could not be loaded.");
        }
    }
}
