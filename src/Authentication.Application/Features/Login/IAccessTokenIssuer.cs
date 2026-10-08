namespace Authentication.Application.Features.Login;

public interface IAccessTokenIssuer
{
    AccessToken Issue(AuthenticatedIdentity identity);
}
