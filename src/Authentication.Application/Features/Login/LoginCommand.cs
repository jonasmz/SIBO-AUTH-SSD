namespace Authentication.Application.Features.Login;

public sealed record LoginCommand(string Email, string Password);
