namespace Coworkee.Client.Blazor.Api;

/// <summary>
/// A refused API call. <see cref="Exception.Message"/> is what a person can read: the validation messages, else the problem's title, else its code,
/// so a component that shows only the message (MudExFileManager shows "ApiException: {Message}") names the rule that refused the request.
/// </summary>
public sealed class ApiException(int status, string? code, IReadOnlyDictionary<string, string[]>? errors, string? title = null)
    : Exception(Describe(status, code, errors, title))
{
    public int Status { get; } = status;

    public string? Code { get; } = code;

    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

    /// <summary>The message of the problem, e.g. which code relies on a role that cannot be deleted.</summary>
    public string? Title { get; } = title;

    private static string Describe(int status, string? code, IReadOnlyDictionary<string, string[]>? errors, string? title) =>
        errors?.SelectMany(e => e.Value).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList() is { Count: > 0 } messages
            ? string.Join(" ", messages)
            : !string.IsNullOrWhiteSpace(title) ? title : code ?? $"Request failed with status {status}.";
}
