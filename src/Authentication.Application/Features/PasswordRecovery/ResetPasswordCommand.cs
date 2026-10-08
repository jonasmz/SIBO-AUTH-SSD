namespace Authentication.Application.Features.PasswordRecovery;

public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword);
