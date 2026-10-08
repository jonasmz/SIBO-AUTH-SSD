namespace Authentication.Application.Features.PasswordRecovery;

/// <param name="Email">Address of the account the token was issued for.</param>
/// <param name="Token">The reset token, already encoded for delivery by email.</param>
public sealed record PasswordResetTicket(string Email, string Token);
