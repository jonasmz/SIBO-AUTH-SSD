namespace Authentication.Application.Features.Users;

public sealed record CreateUserCommand(string Email, string Password, bool Enabled, IReadOnlyList<string> Roles);
