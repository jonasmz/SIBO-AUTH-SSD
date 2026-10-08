namespace Authentication.Application.Features.Administration;

/// <summary>Outcome of an administrative operation: a value, or an error with a fixed, safe detail.</summary>
public sealed class AdministrationResult<T>
{
    public AdministrationResult(T value)
    {
        Value = value;
        Detail = string.Empty;
    }

    public AdministrationResult(AdministrationError error, string detail)
    {
        Error = error;
        Detail = detail;
    }

    public T? Value { get; }

    public AdministrationError? Error { get; }

    public string Detail { get; }

    public bool Succeeded => Error is null;
}
