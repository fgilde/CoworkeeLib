namespace Coworkee.Core.Results;

/// <param name="Template">With <paramref name="Arguments"/>: the message before they were filled in, so a client translates it like its own texts.</param>
public sealed record Error(
    string Code, string Message, ErrorKind Kind, IReadOnlyDictionary<string, string[]>? Details = null, string? Template = null, IReadOnlyList<string>? Arguments = null)
{
    public static Error Validation(IReadOnlyDictionary<string, string[]> details) =>
        new("validation", "One or more validation errors occurred.", ErrorKind.Validation, details);

    /// <summary>One field's error; <see cref="Message"/> is the message itself, so callers need not dig in <see cref="Details"/>.</summary>
    public static Error Validation(string field, string message) =>
        new("validation", message, ErrorKind.Validation, new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    /// <summary>A conflict whose message has variable parts: <paramref name="template"/> ("{0} ...") stays translatable.</summary>
    public static Error Conflict(string code, string template, params string[] arguments) =>
        new(code, string.Format(System.Globalization.CultureInfo.InvariantCulture, template, arguments), ErrorKind.Conflict, null, template, arguments);

    public static Error Unexpected(string code, string message) => new(code, message, ErrorKind.Unexpected);
}
