namespace Authentication.Application.Features.Login;

public sealed class LoginHandler(IIdentityCredentialValidator validator, IAccessTokenIssuer issuer)
{
    public async Task<LoginOutcome> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var identity = await validator.ValidateAsync(command.Email, command.Password, cancellationToken);

        return identity is null
            ? LoginOutcome.InvalidCredentials
            : LoginOutcome.Success(issuer.Issue(identity));
    }
}
