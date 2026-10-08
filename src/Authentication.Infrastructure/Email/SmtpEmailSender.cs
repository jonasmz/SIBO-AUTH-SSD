using System.Diagnostics;
using Authentication.Application.Features.PasswordRecovery;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Authentication.Infrastructure.Email;

/// <summary>The only <see cref="IEmailSender"/>: plain-text SMTP delivery through MailKit, confined to Infrastructure.</summary>
public sealed partial class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    TimeProvider timeProvider,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var smtp = options.Value;

        try
        {
            using var mime = BuildMessage(smtp, message);
            using var client = new SmtpClient();

            // No protocol logger is attached: it would record credentials and message bodies.
            await client.ConnectAsync(smtp.Host, smtp.Port, ToSocketOptions(smtp.Security), cancellationToken);
            if (!string.IsNullOrWhiteSpace(smtp.Username))
            {
                await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Only safe diagnostics: never the recipient, subject, body, credentials, or server reply text.
            LogDeliveryFailed(
                logger,
                exception.GetType().Name,
                exception is SmtpCommandException command ? (int)command.StatusCode : null,
                smtp.Host,
                smtp.Port,
                timeProvider.GetUtcNow(),
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString());

            return false;
        }
    }

    /// <summary>Builds the plain-text MIME message from the configured sender to the single recipient.</summary>
    public static MimeMessage BuildMessage(SmtpOptions smtp, EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(smtp);
        ArgumentNullException.ThrowIfNull(message);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(smtp.SenderName, smtp.SenderAddress));
        mime.To.Add(MailboxAddress.Parse(message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.TextBody };

        return mime;
    }

    private static SecureSocketOptions ToSocketOptions(SmtpSecurity? security) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.None
    };

    [LoggerMessage(LogLevel.Warning, "EmailDeliveryFailed: password recovery email was not delivered ({ExceptionType}, SMTP status {StatusCode}) via {Host}:{Port} at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    private static partial void LogDeliveryFailed(ILogger logger, string exceptionType, int? statusCode, string host, int port, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);
}
