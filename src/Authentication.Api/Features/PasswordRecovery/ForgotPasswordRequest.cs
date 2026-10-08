using System.ComponentModel.DataAnnotations;

namespace Authentication.Api.Features.PasswordRecovery;

public sealed record ForgotPasswordRequest(string? Email)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Email) &&
        new EmailAddressAttribute().IsValid(Email);
}
