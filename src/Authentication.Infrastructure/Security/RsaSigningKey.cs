using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Authentication.Infrastructure.Security;

/// <summary>
/// Loads the private PEM once. It signs access tokens and exposes only the public half for
/// validating the administrative bearer tokens, so the key file is read a single time.
/// </summary>
public sealed class RsaSigningKey : IDisposable
{
    private readonly RSA _privateKey;
    private readonly RSA _publicKey;

    public RsaSigningKey(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _privateKey = Load(options.Value.PrivateKeyPath);
        _publicKey = RSA.Create();
        _publicKey.ImportParameters(_privateKey.ExportParameters(includePrivateParameters: false));

        SigningCredentials = new SigningCredentials(new RsaSecurityKey(_privateKey), SecurityAlgorithms.RsaSha256);
        PublicKey = new RsaSecurityKey(_publicKey);
    }

    public SigningCredentials SigningCredentials { get; }

    public RsaSecurityKey PublicKey { get; }

    public void Dispose()
    {
        _publicKey.Dispose();
        _privateKey.Dispose();
    }

    private static RSA Load(string path)
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
            throw new InvalidOperationException(
                $"The signing key referenced by '{JwtOptions.SectionName}:PrivateKeyPath' could not be loaded as an RSA private key.");
        }
    }
}
