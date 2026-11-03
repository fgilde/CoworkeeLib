namespace Coworkee.Client.Blazor.Api;

public sealed class ApiException(int status, string? code, IReadOnlyDictionary<string, string[]>? errors)
    : Exception(code ?? $"Request failed with status {status}.")
{
    public int Status { get; } = status;

    public string? Code { get; } = code;

    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}
