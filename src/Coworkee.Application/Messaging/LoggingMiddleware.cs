using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Coworkee.Application.Messaging;

internal sealed partial class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.Logging;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await next();
            LogHandled(typeof(TRequest).Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(exception, typeof(TRequest).Name);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {Request} in {ElapsedMs} ms")]
    private partial void LogHandled(string request, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Request {Request} failed")]
    private partial void LogFailed(Exception exception, string request);
}
