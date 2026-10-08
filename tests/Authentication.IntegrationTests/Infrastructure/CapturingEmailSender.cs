using Authentication.Application.Features.PasswordRecovery;

namespace Authentication.IntegrationTests.Infrastructure;

/// <summary>The only test double: records what the host asked to deliver at the email boundary.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _messages = [];

    public IReadOnlyList<EmailMessage> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }

        return Task.FromResult(true);
    }

    /// <summary>The reset token carried by a recovery message: the last whitespace-separated line-token of the body.</summary>
    public static string TokenOf(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.TextBody.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .First(line => line.StartsWith("Token:", StringComparison.Ordinal))["Token:".Length..].Trim();
    }
}
