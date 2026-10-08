namespace Authentication.Application.Features.PasswordRecovery;

public sealed class ForgotPasswordHandler(IPasswordRecovery recovery, IEmailSender emailSender)
{
    private const string Subject = "Password reset instructions";

    /// <summary>
    /// Issues a token for an existing, enabled account and requests its delivery. Whether delivery
    /// succeeds is deliberately not reported, so the caller can never learn that an account exists.
    /// </summary>
    public async Task HandleAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var ticket = await recovery.IssueResetTokenAsync(email, cancellationToken);
        if (ticket is null)
        {
            return;
        }

        _ = await emailSender.SendAsync(new EmailMessage(ticket.Email, Subject, BodyFor(ticket.Token)), cancellationToken);
    }

    private static string BodyFor(string token) =>
        "A password reset was requested for this address." + Environment.NewLine + Environment.NewLine +
        "To choose a new password, send the token below together with your email address and the new " +
        "password to POST /api/auth/reset-password." + Environment.NewLine + Environment.NewLine +
        "Token: " + token + Environment.NewLine + Environment.NewLine +
        "The token is temporary and can be used once. If you did not request this, ignore this message.";
}
