namespace Authentication.Application.Features.Passwords;

public enum ChangePasswordOutcome
{
    Changed,
    InvalidCurrentPassword,
    InvalidNewPassword,
    Invalid
}
