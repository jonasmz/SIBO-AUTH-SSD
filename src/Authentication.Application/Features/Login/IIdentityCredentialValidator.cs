namespace Authentication.Application.Features.Login;

public interface IIdentityCredentialValidator
{
    /// <summary>
    /// Returns the authenticated identity, or <see langword="null"/> for any invalid-credential state.
    /// </summary>
    Task<AuthenticatedIdentity?> ValidateAsync(string email, string password, CancellationToken cancellationToken);
}
