using System.Net;

namespace Coworkee.Client;

/// <summary>A failed API call with the problem details the API sent.</summary>
public sealed class CoworkeeApiException(HttpStatusCode status, string message, string? code, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    public string? Code { get; } = code;

    /// <summary>Validation messages per field, when the API rejected the input.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}
