using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Authentication.IntegrationTests.Infrastructure;

public enum TestTokenAlgorithm
{
    Rs256,
    Rs512,
    None
}

public sealed record TestTokenRequest
{
    public string Subject { get; init; } = "7f0b4a3e-5c1d-4e8a-9b6f-0a1c2d3e4f02";

    public string Issuer { get; init; } = TestTokenMinter.DefaultIssuer;

    public string Audience { get; init; } = TestTokenMinter.DefaultAudience;

    public IReadOnlyList<string> Roles { get; init; } = ["Administrator"];

    public DateTimeOffset? IssuedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public TestTokenAlgorithm Algorithm { get; init; } = TestTokenAlgorithm.Rs256;
}

/// <summary>Signs test tokens with a disposable RSA pair; the public half is what consumers receive.</summary>
public sealed class TestTokenMinter : IDisposable
{
    public const string DefaultIssuer = "https://auth-api.test";
    public const string DefaultAudience = "authentication-api-tests";

    private readonly RSA _rsa;

    /// <summary>Creates a minter with a fresh disposable key pair.</summary>
    public TestTokenMinter()
    {
        _rsa = RSA.Create(3072);
    }

    /// <summary>Creates a minter that signs with an existing private key, for example the Authentication API test key.</summary>
    public TestTokenMinter(string privateKeyPem)
    {
        _rsa = RSA.Create();
        _rsa.ImportFromPem(privateKeyPem);
    }

    public string PublicKeyPem => _rsa.ExportSubjectPublicKeyInfoPem();

    public string PrivateKeyPem => _rsa.ExportRSAPrivateKeyPem();

    public string Mint(TestTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var issuedAt = request.IssuedAt ?? now;
        var expiresAt = request.ExpiresAt ?? issuedAt.AddMinutes(15);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.Subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange(request.Roles.Select(role => new Claim("role", role)));

        if (request.Algorithm == TestTokenAlgorithm.None)
        {
            return MintUnsigned(claims, request, issuedAt, expiresAt);
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = request.Issuer,
            Audience = request.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(_rsa),
                request.Algorithm == TestTokenAlgorithm.Rs512
                    ? SecurityAlgorithms.RsaSha512
                    : SecurityAlgorithms.RsaSha256)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            }
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    private static string MintUnsigned(
        List<Claim> claims,
        TestTokenRequest request,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        var header = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        var payload = new Dictionary<string, object>
        {
            ["sub"] = request.Subject,
            ["iss"] = request.Issuer,
            ["aud"] = request.Audience,
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["role"] = claims.Where(claim => claim.Type == "role").Select(claim => claim.Value).ToArray()
        };

        return $"{header}.{Base64UrlEncoder.Encode(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload))}.";
    }
}
