namespace Authentication.Api.Features.Passwords;

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(CurrentPassword) &&
        !string.IsNullOrWhiteSpace(NewPassword);
}
