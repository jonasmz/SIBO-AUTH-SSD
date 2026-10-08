namespace Authentication.Application.Features.Sessions;

public interface IRefreshSessionLookup
{
    Task<RefreshSessionLookupResult> FindAsync(byte[] tokenHash, CancellationToken cancellationToken);
}
