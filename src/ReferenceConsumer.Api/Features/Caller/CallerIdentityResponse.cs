namespace ReferenceConsumer.Api.Features.Caller;

public sealed record CallerIdentityResponse(string Service, string Subject, IReadOnlyList<string> Roles);
