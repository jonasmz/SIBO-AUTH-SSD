namespace Authentication.Application.Features.Sessions;

public sealed record LogoutSessionOutcome
{
    public static LogoutSessionOutcome Completed { get; } = new();
}
