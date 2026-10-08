using System.Text.Json;

namespace Authentication.Api.Features.Administration;

public static class AdministrationRequests
{
    /// <summary>
    /// Reads a JSON request body. Returns <see langword="null"/> for a non-JSON content type or a body
    /// that is malformed, empty, or carries an unmapped member.
    /// </summary>
    public static async Task<T?> ReadAsync<T>(HttpRequest request, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await request.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
