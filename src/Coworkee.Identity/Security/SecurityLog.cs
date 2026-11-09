using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Coworkee.Application.Auditing;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Security;

internal sealed class SecurityLog(IServiceScopeFactory scopes, IHttpContextAccessor http, ICurrentUser currentUser, TimeProvider clock) : ISecurityLog
{
    public async Task WriteAsync(SecurityLogEntry entry, CancellationToken cancellationToken = default)
    {
        var row = Create(entry);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        // sign-in pages know the user, not his organisation
        if (row.TenantId is null && row.UserId is { } user)
        {
            row.TenantId = await db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == user).Select(u => (Guid?)u.TenantId).FirstOrDefaultAsync(cancellationToken);
        }

        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The entry with what the request knows: the acting user (when it is not the subject), address, browser, client and trace.</summary>
    internal SecurityEvent Create(SecurityLogEntry entry)
    {
        var context = http.HttpContext;
        var subject = entry.UserId ?? currentUser.UserId;
        return new SecurityEvent
        {
            At = clock.GetUtcNow(),
            TenantId = entry.TenantId ?? currentUser.TenantId,
            UserId = subject,
            ActorId = currentUser.UserId is { } actor && actor != subject ? actor : null,
            ImpersonatorId = entry.ImpersonatorId ?? currentUser.ImpersonatorId,
            Action = Cut(entry.Action, SecurityEvent.ActionLength)!,
            Succeeded = entry.Succeeded,
            Reason = Cut(entry.Reason, 200),
            ClientId = Cut(currentUser.ClientId ?? context?.User.FindFirstValue("client_id"), 200),
            Ip = context?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Cut(context?.Request.Headers.UserAgent.ToString(), 512),
            TraceId = Activity.Current?.TraceId.ToString() ?? context?.TraceIdentifier,
            Details = entry.Details is null ? null : Cut(JsonSerializer.Serialize(entry.Details, JsonSerializerOptions.Web), 4000),
        };
    }

    private static string? Cut(string? text, int length) => string.IsNullOrEmpty(text) ? null : text.Length <= length ? text : text[..length];
}
