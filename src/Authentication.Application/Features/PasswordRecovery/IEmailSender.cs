namespace Authentication.Application.Features.PasswordRecovery;

public interface IEmailSender
{
    /// <summary>Requests delivery of one message; <see langword="false"/> means it was not delivered (already logged by the adapter).</summary>
    Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
