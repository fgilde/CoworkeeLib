using Coworkee.Application.Messaging;
using Microsoft.AspNetCore.Http;

namespace Coworkee.Auditing.Requests;

/// <summary>Notes the first request an endpoint dispatches (the one it maps to) for the request log.</summary>
internal sealed class RequestTypeCapture(IHttpContextAccessor accessor) : IRequestMiddleware
{
    public int Order => 0;

    public Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        if (accessor.HttpContext?.Features.Get<RequestLogFeature>() is { RequestType: null } feature)
        {
            feature.RequestType = typeof(TRequest).Name;
        }

        return next();
    }
}
