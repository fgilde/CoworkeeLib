using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.OData;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Auditing.Requests;

[AiTool(Exclude = true)]
[RequiresPermission(AuditPermissions.Requests)]
public sealed record GetRequestLogEntry(Guid Id) : IQuery<Result<RequestLogDetailDto>>;

internal sealed class GetRequestLogEntryValidator : AbstractValidator<GetRequestLogEntry>
{
    public GetRequestLogEntryValidator() => RuleFor(q => q.Id).NotEmpty();
}

/// <summary>A request of the own tenant with its headers; the bodies only for holders of <see cref="AuditPermissions.RequestBodies"/>.</summary>
internal sealed class GetRequestLogEntryHandler(CoworkeeDbContext db, IODataEntityFilter<RequestLogEntry> tenant, IPermissionChecker permissions)
    : IHandler<GetRequestLogEntry, Result<RequestLogDetailDto>>
{
    public async Task<Result<RequestLogDetailDto>> HandleAsync(GetRequestLogEntry request, CancellationToken cancellationToken)
    {
        var rows = await tenant.ApplyAsync(db.Set<RequestLogEntry>().AsNoTracking(), cancellationToken);
        if (await rows.SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken) is not { } e)
        {
            return Error.NotFound("request-log.not-found", "The request is not in the log.");
        }

        var bodies = await permissions.IsGrantedAsync(AuditPermissions.RequestBodies, cancellationToken);
        return new RequestLogDetailDto(ToDto(e), Headers(e.RequestHeaders), Headers(e.ResponseHeaders), bodies ? e.RequestBody : null, bodies ? e.ResponseBody : null, !bodies);
    }

    private static RequestLogDto ToDto(RequestLogEntry e) => new(e.Id, e.At, e.TenantId, e.UserId, e.ImpersonatorId, e.ClientId, e.ApiKeyId, e.Method, e.Path, e.QueryString,
        e.Route, e.RequestType, e.StatusCode, e.DurationMs, e.Ip, e.UserAgent, e.TraceId, e.Exception);

    private static Dictionary<string, string> Headers(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
}
