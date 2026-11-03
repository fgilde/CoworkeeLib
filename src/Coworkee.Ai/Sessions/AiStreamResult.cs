using System.Text.Json;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Http;

namespace Coworkee.Ai.Sessions;

/// <summary>Writes the events of a turn as JSON lines, flushed one by one; the last line is the saved message or the error.</summary>
internal sealed class AiStreamResult(Func<Func<AiStreamEvent, Task>, Task<AiStreamEvent>> run) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.ContentType = "application/x-ndjson";
        response.Headers.CacheControl = "no-cache";
        httpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
        var connected = true;

        async Task WriteAsync(AiStreamEvent streamEvent)
        {
            if (!connected)
            {
                return;
            }

            try
            {
                await response.WriteAsync(JsonSerializer.Serialize(streamEvent, JsonSerializerOptions.Web) + "\n", CancellationToken.None);
                await response.Body.FlushAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // the browser left: the turn goes on and is saved for the other tabs
                connected = false;
            }
        }

        await WriteAsync(await run(WriteAsync));
    }
}
