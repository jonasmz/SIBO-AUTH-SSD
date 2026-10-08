using System.ComponentModel.DataAnnotations;

namespace Authentication.Api.Features.Login;

public sealed record LoginRequest(string? Email, string? Password)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(Password) &&
        new EmailAddressAttribute().IsValid(Email);
}
