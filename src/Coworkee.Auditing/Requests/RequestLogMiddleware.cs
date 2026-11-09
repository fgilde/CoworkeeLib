using System.Diagnostics;
using System.Security.Claims;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing.Requests;

/// <summary>
/// Captures every API request of this host for the request log: who, what, how long, the answer, headers and JSON/form bodies (the first bytes only,
/// passed through, never buffered). Redaction and saving happen in <see cref="RequestLogWriter"/>.
/// </summary>
internal static class RequestLogMiddleware
{
    // probes, telemetry, API docs and the realtime connection are no API calls anyone looks for
    private static readonly string[] Skipped = ["/health", "/alive", "/metrics", "/openapi", "/swagger", "/hubs"];

    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!Logs(context))
        {
            await next(context);
            return;
        }

        var request = context.Request;
        var policy = await RequestLogPolicy.GetAsync(context.RequestServices, context.RequestAborted);
        if (!(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ? policy.Reading : policy.Writing))
        {
            await next(context);
            return;
        }

        var at = DateTimeOffset.UtcNow;
        var started = Stopwatch.GetTimestamp();
        var response = context.Response;
        var feature = new RequestLogFeature();
        context.Features.Set(feature);
        var requestBody = policy.MaxBodyBytes > 0 && RequestLogContent.IsCaptured(request.ContentType) ? new CaptureStream(request.Body, policy.MaxBodyBytes) : null;
        if (requestBody is not null)
        {
            request.Body = requestBody;
        }

        var originalBody = response.Body;
        var responseBody = policy.MaxBodyBytes > 0 ? new CaptureStream(originalBody, policy.MaxBodyBytes, () => RequestLogContent.IsCaptured(response.ContentType)) : null;
        if (responseBody is not null)
        {
            response.Body = responseBody;
        }

        string? exception = null;
        try
        {
            await next(context);
        }
        catch (Exception e)
        {
            exception = $"{e.GetType().Name}: {e.Message}";
            throw;
        }
        finally
        {
            response.Body = originalBody;
            if (requestBody is not null)
            {
                request.Body = requestBody.Inner;
            }

            context.RequestServices.GetRequiredService<RequestLogWriter>().Write(Capture(context, policy, feature, at, started, exception, requestBody, responseBody));
        }
    }

    private static bool Logs(HttpContext context)
    {
        var path = context.Request.Path;
        if (HttpMethods.IsOptions(context.Request.Method) || Skipped.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var endpoint = context.GetEndpoint();
        return endpoint is null
            ? path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) // unknown API addresses: scans and broken clients
            : endpoint.Metadata.GetMetadata<SkipRequestLogAttribute>() is null;
    }

    private static RequestLogCapture Capture(
        HttpContext context, RequestLogPolicy policy, RequestLogFeature feature, DateTimeOffset at, long started, string? exception, CaptureStream? requestBody, CaptureStream? responseBody)
    {
        var request = context.Request;
        var user = context.RequestServices.GetRequiredService<ICurrentUser>();
        var entry = new RequestLogEntry
        {
            At = at,
            TenantId = user.TenantId,
            UserId = user.UserId,
            ImpersonatorId = user.ImpersonatorId,
            ClientId = Cut(user.ClientId ?? context.User.FindFirstValue("client_id"), 200),
            ApiKeyId = user.ApiKeyId,
            Method = request.Method,
            Path = Cut(request.Path.Value, RequestLogModel.PathLength) ?? "/",
            Route = Cut((context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText, 300),
            RequestType = feature.RequestType,
            StatusCode = exception is null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError,
            DurationMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            Ip = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Cut(request.Headers.UserAgent.ToString(), 512),
            TraceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            Exception = Cut(exception, 2000),
        };
        return new RequestLogCapture(entry, policy, request.QueryString.Value, Headers(request.Headers), Headers(context.Response.Headers), request.ContentType,
            context.Response.ContentType, requestBody?.Take() ?? default, responseBody?.Take() ?? default);
    }

    private static KeyValuePair<string, string>[] Headers(IHeaderDictionary headers) => [.. headers.Select(h => KeyValuePair.Create(h.Key, h.Value.ToString()))];

    private static string? Cut(string? text, int length) => string.IsNullOrEmpty(text) ? null : text.Length <= length ? text : text[..length];
}

/// <summary>Set while the request log captures a request; <see cref="RequestTypeCapture"/> notes the request the endpoint dispatched.</summary>
internal sealed class RequestLogFeature
{
    public string? RequestType { get; set; }
}
