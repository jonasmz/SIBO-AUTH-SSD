using System.Text.Json.Serialization;

namespace Authentication.Api.Features.Users;

/// <summary>The complete set of role names the user must end up with; an empty list is valid.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceUserRolesRequest(string[]? Roles)
{
    private const int MaxLength = 256;

    public bool IsValid =>
        Roles is not null && Roles.All(role => !string.IsNullOrWhiteSpace(role) && role.Length <= MaxLength);
}
