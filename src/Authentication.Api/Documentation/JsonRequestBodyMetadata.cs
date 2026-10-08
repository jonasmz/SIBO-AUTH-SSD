namespace Authentication.Api.Documentation;

/// <summary>
/// Documentation-only marker for endpoints that read their JSON body themselves. Unlike <c>Accepts&lt;T&gt;</c>, it
/// adds no content-type metadata, so the framework never answers <c>415</c> in place of the endpoint's own
/// <c>400</c> problem details.
/// </summary>
internal sealed record JsonRequestBodyMetadata(Type BodyType);
