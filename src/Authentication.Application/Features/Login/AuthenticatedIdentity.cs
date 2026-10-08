namespace Authentication.Application.Features.Login;

public sealed record AuthenticatedIdentity(string UserId, string Email, IReadOnlyList<string> Roles);
