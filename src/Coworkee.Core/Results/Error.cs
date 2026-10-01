namespace Coworkee.Core.Results;

public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Unexpected,
}

public sealed record Error(string Code, string Message, ErrorKind Kind, IReadOnlyDictionary<string, string[]>? Details = null)
{
    public static Error Validation(IReadOnlyDictionary<string, string[]> details) =>
        new("validation", "One or more validation errors occurred.", ErrorKind.Validation, details);

    public static Error Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Unexpected(string code, string message) => new(code, message, ErrorKind.Unexpected);
}
