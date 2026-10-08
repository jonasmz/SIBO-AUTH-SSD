using System.Text.Json.Serialization;

namespace Authentication.Api.Features.Roles;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RoleNameRequest(string? Name)
{
    private const int MaxLength = 256;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Name.Trim().Length <= MaxLength;

    public string TrimmedName => Name!.Trim();
}
