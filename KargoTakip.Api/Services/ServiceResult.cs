namespace KargoTakip.Api.Services;

public enum ResultKind { Ok, NotFound, Invalid, Conflict, Forbidden }

/// <summary>What a service call returns: either a value or a reason why the call was refused.</summary>
public record ServiceResult<T>(
    ResultKind Kind,
    T? Value = default,
    Dictionary<string, string[]>? Errors = null,
    string? Message = null)
{
    public static ServiceResult<T> Ok(T value) => new(ResultKind.Ok, value);
    public static ServiceResult<T> NotFound() => new(ResultKind.NotFound);
    public static ServiceResult<T> Invalid(Dictionary<string, string[]> errors) => new(ResultKind.Invalid, Errors: errors);
    public static ServiceResult<T> Conflict(string message) => new(ResultKind.Conflict, Message: message);
    public static ServiceResult<T> Forbidden(string? message = null) => new(ResultKind.Forbidden, Message: message);
}
