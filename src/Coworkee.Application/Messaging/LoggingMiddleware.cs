using System.Diagnostics;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Application.Messaging;

internal sealed partial class LoggingMiddleware(ILogger<LoggingMiddleware> logger, IOptions<MessagingOptions> options, IServiceProvider services) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.Logging;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await next();
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed > options.Value.SlowRequestThreshold)
            {
                LogSlow(typeof(TRequest).Name, elapsed.TotalMilliseconds, services.GetService<ICurrentUser>()?.UserId);
            }
            else
            {
                LogHandled(typeof(TRequest).Name, elapsed.TotalMilliseconds);
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(exception, typeof(TRequest).Name, services.GetService<ICurrentUser>()?.UserId);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {Request} in {ElapsedMs} ms")]
    private partial void LogHandled(string request, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slow request {Request} took {ElapsedMs} ms for user {UserId}")]
    private partial void LogSlow(string request, double elapsedMs, Guid? userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Request {Request} failed for user {UserId}")]
    private partial void LogFailed(Exception exception, string request, Guid? userId);
}
