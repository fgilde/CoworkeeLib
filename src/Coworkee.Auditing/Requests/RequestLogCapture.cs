namespace Coworkee.Auditing.Requests;

/// <summary>What the middleware took from a request, unredacted; the writer redacts and stores it off the request path.</summary>
internal sealed record RequestLogCapture(
    RequestLogEntry Entry,
    RequestLogPolicy Policy,
    string? QueryString,
    KeyValuePair<string, string>[] RequestHeaders,
    KeyValuePair<string, string>[] ResponseHeaders,
    string? RequestContentType,
    string? ResponseContentType,
    (byte[]? Bytes, bool Truncated) RequestBody,
    (byte[]? Bytes, bool Truncated) ResponseBody)
{
    public RequestLogEntry ToEntry()
    {
        Entry.QueryString = RequestLogRedaction.Query(QueryString, Policy);
        Entry.RequestHeaders = RequestLogRedaction.Headers(RequestHeaders, Policy);
        Entry.ResponseHeaders = RequestLogRedaction.Headers(ResponseHeaders, Policy);
        Entry.RequestBody = RequestLogRedaction.Body(RequestBody.Bytes, RequestBody.Truncated, RequestContentType, Policy);
        Entry.ResponseBody = RequestLogRedaction.Body(ResponseBody.Bytes, ResponseBody.Truncated, ResponseContentType, Policy);
        return Entry;
    }
}
