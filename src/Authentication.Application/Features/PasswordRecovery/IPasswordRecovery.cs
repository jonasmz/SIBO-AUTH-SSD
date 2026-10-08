namespace Authentication.Application.Features.PasswordRecovery;

public interface IPasswordRecovery
{
    /// <summary>Issues a temporary reset token for an existing, enabled account; <see langword="null"/> for any other address.</summary>
    Task<PasswordResetTicket?> IssueResetTokenAsync(string email, CancellationToken cancellationToken);

    /// <summary>Replaces the password with a valid token and ends every renewable session as one outcome.</summary>
    Task<ResetPasswordOutcome> ResetAsync(ResetPasswordCommand command, CancellationToken cancellationToken);
}
