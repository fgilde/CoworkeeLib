using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Coworkee.Auditing.Tests;

/// <summary>Endpoints the request log tests call: an echo through the dispatcher, a read, one left out of the log, one that throws.</summary>
internal static class LogTestApi
{
    public static void Map(WebApplication app)
    {
        var test = app.MapCoworkeeApi("/api/v1/test").RequireAuthorization();
        test.MapPost("/echo", (JsonElement body, IDispatcher d, CancellationToken ct) => d.SendAsync(new Echo(body), ct).ToHttpResult());
        test.MapGet("/echo", (string? q) => Results.Ok(new { q, accessToken = "t0ken" }));
        test.MapPost("/quiet", () => Results.Ok()).SkipRequestLog();
        test.MapPost("/fail", IResult () => throw new InvalidOperationException("boom"));
    }
}

public sealed record Echo(JsonElement Body) : IQuery<Result<JsonElement>>;

internal sealed class EchoHandler : IHandler<Echo, Result<JsonElement>>
{
    public Task<Result<JsonElement>> HandleAsync(Echo request, CancellationToken cancellationToken) => Task.FromResult(Result<JsonElement>.Success(request.Body));
}
