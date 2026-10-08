using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Authentication.Api.Features.Users;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateUserRequest(string? Email, string? Password, bool? Enabled, string[]? Roles)
{
    private const int MaxLength = 256;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Email) &&
        Email.Length <= MaxLength &&
        new EmailAddressAttribute().IsValid(Email) &&
        !string.IsNullOrWhiteSpace(Password) &&
        (Roles is null || Roles.All(role => !string.IsNullOrWhiteSpace(role) && role.Length <= MaxLength));
}
