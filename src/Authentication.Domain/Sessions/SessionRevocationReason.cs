namespace Authentication.Domain.Sessions;

public enum SessionRevocationReason
{
    Replay,
    Logout,
    UserDisabled,
    Administrator,
    PasswordChanged,
    PasswordReset
}
