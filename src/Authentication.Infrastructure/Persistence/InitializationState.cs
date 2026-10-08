namespace Authentication.Infrastructure.Persistence;

public sealed class InitializationState
{
    private int _isReady;

    public bool IsReady => Volatile.Read(ref _isReady) == 1;

    public void MarkReady()
    {
        Interlocked.Exchange(ref _isReady, 1);
    }

    public void MarkNotReady()
    {
        Interlocked.Exchange(ref _isReady, 0);
    }
}
