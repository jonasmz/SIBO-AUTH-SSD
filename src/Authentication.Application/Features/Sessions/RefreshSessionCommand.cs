namespace Authentication.Application.Features.Sessions;

public sealed record RefreshSessionCommand(byte[] PresentedTokenHash, byte[] ReplacementTokenHash);
