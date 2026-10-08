using System.Data.Common;
using Authentication.Application.Features.Administration;

namespace Authentication.Api.Features.Administration;

public static class AdministrationResults
{
    public static IResult InvalidRequest() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Bad Request",
        detail: "The request is invalid.");

    public static IResult ToOk<T>(this AdministrationResult<T> result) =>
        result.Succeeded ? Results.Ok(result.Value) : Failure(result);

    public static IResult ToCreated<T>(this AdministrationResult<T> result, Func<T, string> location) =>
        result.Succeeded ? Results.Created(location(result.Value!), result.Value) : Failure(result);

    public static IResult ToNoContent<T>(this AdministrationResult<T> result) =>
        result.Succeeded ? Results.NoContent() : Failure(result);

    /// <summary>Runs an administrative action, answering <c>503</c> when the database is unavailable.</summary>
    public static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return await action();
        }
        catch (DbException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Service Unavailable",
                detail: "The service is not ready.");
        }
    }

    private static IResult Failure<T>(AdministrationResult<T> result)
    {
        var (status, title) = result.Error switch
        {
            AdministrationError.NotFound => (StatusCodes.Status404NotFound, "Not Found"),
            AdministrationError.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (StatusCodes.Status400BadRequest, "Bad Request")
        };

        return Results.Problem(statusCode: status, title: title, detail: result.Detail);
    }
}
