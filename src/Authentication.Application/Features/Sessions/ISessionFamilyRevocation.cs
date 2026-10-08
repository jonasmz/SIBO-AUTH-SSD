using Authentication.Domain.Sessions;

namespace Authentication.Application.Features.Sessions;

public interface ISessionFamilyRevocation
{
    Task RevokeFamilyAsync(string familyId, DateTimeOffset revokedAtUtc, SessionRevocationReason reason, CancellationToken cancellationToken);
}
