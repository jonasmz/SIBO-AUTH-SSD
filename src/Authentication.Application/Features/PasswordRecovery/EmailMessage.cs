namespace Authentication.Application.Features.PasswordRecovery;

/// <summary>A plain-text message addressed to one recipient.</summary>
public sealed record EmailMessage(string ToAddress, string Subject, string TextBody);
