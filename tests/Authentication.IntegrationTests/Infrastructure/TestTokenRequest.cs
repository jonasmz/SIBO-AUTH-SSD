namespace Authentication.IntegrationTests.Infrastructure;

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
