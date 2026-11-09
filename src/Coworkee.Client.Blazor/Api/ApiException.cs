namespace Coworkee.Client.Blazor.Api;

public sealed class ApiException(int status, string? code, IReadOnlyDictionary<string, string[]>? errors, string? title = null)
    : Exception(code ?? $"Request failed with status {status}.")
{
    public int Status { get; } = status;

    public string? Code { get; } = code;

    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

    /// <summary>The message of the problem, e.g. which code relies on a role that cannot be deleted.</summary>
    public string? Title { get; } = title;
}
