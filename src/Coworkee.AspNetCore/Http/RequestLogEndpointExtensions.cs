using Microsoft.AspNetCore.Builder;

namespace Coworkee.AspNetCore.Http;

/// <summary>Leaves an endpoint out of the request log (Coworkee.Auditing), e.g. a poll that runs every few seconds or one that receives files.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipRequestLogAttribute : Attribute;

public static class RequestLogEndpointExtensions
{
    /// <summary>Leaves the endpoints out of the request log.</summary>
    public static TBuilder SkipRequestLog<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.WithMetadata(new SkipRequestLogAttribute());
}
