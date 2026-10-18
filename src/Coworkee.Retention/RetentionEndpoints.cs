using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Retention;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Retention;

internal static class RetentionEndpoints
{
    public static void MapRetentionEndpoints(this IEndpointRouteBuilder app)
    {
        var retention = app.MapCoworkeeApi("/api/v1/retention").WithTags("Retention").RequireAuthorization();
        retention.MapGet("/", (bool? counts, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRetentionTargetsQuery(counts ?? true), ct).ToHttpResult());
        retention.MapGet("/{key}/count", (string key, int days, IDispatcher d, CancellationToken ct) => d.SendAsync(new CountRetentionQuery(key, days), ct).ToHttpResult());
        retention.MapPut("/", (Dictionary<string, int?> body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetRetentionDaysCommand(body), ct).ToHttpResult());
        retention.MapPost("/{key}/run", RunAsync).Produces(StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> RunAsync(string key, RunRetentionRequest body, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RunRetentionCommand(key, body.Days), cancellationToken);
        return result.IsSuccess ? Results.Accepted() : result.Error!.ToProblem();
    }
}
