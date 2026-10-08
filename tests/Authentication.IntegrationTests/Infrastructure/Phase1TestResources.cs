using System.Security.Cryptography;

namespace Authentication.IntegrationTests.Infrastructure;

public sealed class Phase1TestResources : IDisposable
{
    public Phase1TestResources()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "authentication-api-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);

        DatabasePath = Path.Combine(RootPath, "auth.db");
        PrivateKeyPath = Path.Combine(RootPath, "jwt-private.pem");
        DataProtectionKeysPath = Path.Combine(RootPath, "dataprotection");
        Directory.CreateDirectory(DataProtectionKeysPath);

        using var rsa = RSA.Create(3072);
        File.WriteAllText(PrivateKeyPath, rsa.ExportRSAPrivateKeyPem());
        PublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
    }

    public string RootPath { get; }

    public string DatabasePath { get; }

    public string PrivateKeyPath { get; }

    public string PublicKeyPem { get; }

    /// <summary>Temporary Data Protection key-ring directory; tokens survive a host recreated on the same one.</summary>
    public string DataProtectionKeysPath { get; }

    public string ConnectionString => $"Data Source={DatabasePath}";

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
