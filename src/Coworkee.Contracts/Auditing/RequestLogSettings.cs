namespace Coworkee.Contracts.Auditing;

public static class RequestLogSettings
{
    /// <summary>Which requests are logged with headers and bodies: <see cref="Reading"/>, <see cref="Writing"/>, both (default) or none (the request log is off).</summary>
    public const string Requests = "RequestLog.Requests";

    /// <summary>The kilobytes kept of each body; longer bodies are cut.</summary>
    public const string MaxBodyKb = "RequestLog.MaxBodyKb";

    /// <summary>More header and field names whose values are replaced by <c>***</c>, comma separated.</summary>
    public const string RedactedNames = "RequestLog.RedactedNames";

    /// <summary>GET, HEAD and OData queries.</summary>
    public const string Reading = "Reading";

    /// <summary>POST, PUT, PATCH and DELETE.</summary>
    public const string Writing = "Writing";
}
