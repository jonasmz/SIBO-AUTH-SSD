namespace Authentication.Application.Features.Sessions;

public interface IRenewableSessionStore
{
    Task<IssuedRefreshSession> IssueAsync(string userId, CancellationToken cancellationToken);
}
