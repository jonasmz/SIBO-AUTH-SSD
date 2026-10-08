namespace Authentication.Application.Features.Sessions;

public sealed record LogoutSessionCommand(byte[]? PresentedTokenHash);
