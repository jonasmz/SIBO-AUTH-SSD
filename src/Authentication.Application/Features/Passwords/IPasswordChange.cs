namespace Authentication.Application.Features.Passwords;

public interface IPasswordChange
{
    /// <summary>
    /// Replaces the user's own password and ends their other renewable sessions as one outcome;
    /// any refusal changes neither the credential nor any session.
    /// </summary>
    Task<ChangePasswordOutcome> ChangeAsync(ChangePasswordCommand command, CancellationToken cancellationToken);
}
