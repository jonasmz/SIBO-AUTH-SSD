using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Authentication.Api.Features.Users;

/// <summary>The email is the only attribute an administrator may update; any other member is rejected.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateUserRequest(string? Email)
{
    private const int MaxLength = 256;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Email) &&
        Email.Length <= MaxLength &&
        new EmailAddressAttribute().IsValid(Email);
}
