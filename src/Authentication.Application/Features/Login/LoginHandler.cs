namespace Authentication.Application.Features.Login;

public sealed class LoginHandler(IIdentityCredentialValidator validator, IAccessTokenIssuer issuer, Authentication.Application.Features.Sessions.IRenewableSessionStore sessions)
{
    public async Task<LoginOutcome> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var identity = await validator.ValidateAsync(command.Email, command.Password, cancellationToken);

        if (identity is null) return LoginOutcome.InvalidCredentials;
        var refresh = await sessions.IssueAsync(identity.UserId, cancellationToken);
        return LoginOutcome.Success(issuer.Issue(identity), refresh.RawCredential, refresh.ExpiresAtUtc);
    }
}
