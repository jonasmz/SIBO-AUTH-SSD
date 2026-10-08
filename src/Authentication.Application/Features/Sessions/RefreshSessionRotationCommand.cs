namespace Authentication.Application.Features.Sessions;

public sealed record RefreshSessionRotationCommand(byte[] PresentedTokenHash, byte[] ReplacementTokenHash, DateTimeOffset ConsumedAtUtc);
