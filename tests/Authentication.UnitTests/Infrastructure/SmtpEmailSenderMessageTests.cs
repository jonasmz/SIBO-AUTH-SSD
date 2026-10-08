using Authentication.Application.Features.PasswordRecovery;
using Authentication.Infrastructure.Email;
using MimeKit;
using Xunit;

namespace Authentication.UnitTests.Infrastructure;

public sealed class SmtpEmailSenderMessageTests
{
    [Fact]
    public void BuildsAPlainTextMessageFromTheConfiguredSenderToTheRecipient()
    {
        var smtp = new SmtpOptions
        {
            Host = "smtp.example.test",
            Port = 587,
            Security = SmtpSecurity.StartTls,
            SenderAddress = "no-reply@example.test",
            SenderName = "Authentication API"
        };
        var message = new EmailMessage("member@example.test", "Password reset instructions", "Token: abc123");

        using var mime = SmtpEmailSender.BuildMessage(smtp, message);

        var from = Assert.IsType<MailboxAddress>(Assert.Single(mime.From));
        Assert.Equal("no-reply@example.test", from.Address);
        Assert.Equal("Authentication API", from.Name);
        var to = Assert.IsType<MailboxAddress>(Assert.Single(mime.To));
        Assert.Equal("member@example.test", to.Address);
        Assert.Equal("Password reset instructions", mime.Subject);
        var body = Assert.IsType<TextPart>(mime.Body);
        Assert.True(body.IsPlain);
        Assert.Contains("Token: abc123", body.Text, StringComparison.Ordinal);
    }
}
