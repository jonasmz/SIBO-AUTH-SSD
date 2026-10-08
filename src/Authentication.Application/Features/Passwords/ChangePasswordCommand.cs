namespace Authentication.Application.Features.Passwords;

/// <param name="UserId">Account identifier taken only from the access token subject.</param>
/// <param name="CurrentPassword">The caller's current password.</param>
/// <param name="NewPassword">The requested replacement password.</param>
/// <param name="PresentedRefreshTokenHash">
/// SHA-256 digest of a well-formed refresh credential sent with the request, used only to select
/// the session to keep; <see langword="null"/> when none accompanied the request.
/// </param>
public sealed record ChangePasswordCommand(
    string UserId,
    string CurrentPassword,
    string NewPassword,
    byte[]? PresentedRefreshTokenHash = null);
