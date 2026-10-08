namespace Authentication.Application.Features.PasswordRecovery;

public enum ResetPasswordOutcome
{
    Reset,
    InvalidToken,
    InvalidNewPassword,
    Invalid
}
