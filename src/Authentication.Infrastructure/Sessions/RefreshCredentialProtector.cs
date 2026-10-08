using System.Security.Cryptography;

namespace Authentication.Infrastructure.Sessions;

public sealed class RefreshCredentialProtector : IDisposable
{
    private readonly RandomNumberGenerator _randomNumberGenerator = RandomNumberGenerator.Create();

    public string CreateRawCredential()
    {
        var bytes = new byte[32];
        _randomNumberGenerator.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    public static bool TryHash(string rawCredential, out byte[] tokenHash)
    {
        tokenHash = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(rawCredential) || rawCredential.Length != 43 ||
            rawCredential.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))) return false;
        try
        {
            var bytes = Base64UrlDecode(rawCredential);
            if (bytes.Length != 32 || !string.Equals(Base64UrlEncode(bytes), rawCredential, StringComparison.Ordinal)) return false;
            tokenHash = SHA256.HashData(bytes);
            return true;
        }
        catch (FormatException) { return false; }
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + "=");

    public void Dispose() => _randomNumberGenerator.Dispose();
}
