using System.ComponentModel.DataAnnotations;

namespace Authentication.Api.Features.PasswordRecovery;

public sealed record ResetPasswordRequest(string? Email, string? Token, string? NewPassword)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Email) &&
        new EmailAddressAttribute().IsValid(Email) &&
        !string.IsNullOrWhiteSpace(Token) &&
        !string.IsNullOrWhiteSpace(NewPassword);
}
