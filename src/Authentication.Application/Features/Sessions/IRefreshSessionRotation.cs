namespace Authentication.Application.Features.Sessions;

public interface IRefreshSessionRotation
{
    Task<RefreshSessionRotationResult> RotateAsync(RefreshSessionRotationCommand command, CancellationToken cancellationToken);
}
