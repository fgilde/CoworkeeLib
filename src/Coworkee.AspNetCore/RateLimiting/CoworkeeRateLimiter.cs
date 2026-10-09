using System.Globalization;
using System.Threading.RateLimiting;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.AspNetCore.RateLimiting;

internal static class CoworkeeRateLimiter
{
    public static void Configure(RateLimiterOptions limiter, IOptions<CoworkeeRateLimitOptions> options)
    {
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.OnRejected = RejectAsync;
        foreach (var (name, rule) in options.Value.Policies)
        {
            limiter.AddPolicy(name, context => Partition(context, rule));
        }

        if (options.Value.Global is { } global)
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => Partition(context, global));
        }
    }

    private static RateLimitPartition<string> Partition(HttpContext context, RateLimitRule rule) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.RequestServices.GetRequiredService<ICurrentUser>().UserId is { } userId ? $"user:{userId}" : $"ip:{context.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = rule.PermitLimit, Window = rule.Window, QueueLimit = 0 });

    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await TypedResults.Problem(
            title: "Too many requests. Please wait a moment and try again.",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "rate_limited" })
            .ExecuteAsync(context.HttpContext);
    }
}
