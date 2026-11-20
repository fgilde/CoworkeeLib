using System.Text;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Webhooks.Tests;

/// <summary>The exact outbox rows of entity webhooks for a fixed sequence of changes, captured before the change scans were merged: they must stay byte-equal.</summary>
public sealed class ChangeGoldenTests(WebhooksApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private readonly HashSet<Guid> _seen = [];
    private readonly StringBuilder _rows = new();
    private readonly Dictionary<string, string> _names = [];

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task The_outbox_rows_of_entity_webhooks()
    {
        var gadget = new Gadget { Name = "Lamp", Price = 10, Cost = 4, TenantId = _setup.TenantId };
        var other = new Gadget { Name = "Desk", Price = 100, Cost = 40, TenantId = _setup.TenantId };
        _names[gadget.Id.ToString()] = "<lamp>";
        _names[other.Id.ToString()] = "<desk>";
        await StepAsync("create", db =>
        {
            db.AddRange(gadget, other);
            return Task.CompletedTask;
        });
        await StepAsync("update", async db => (await db.Set<Gadget>().SingleAsync(g => g.Id == gadget.Id)).Price = 12);
        await StepAsync("same value", async db => (await db.Set<Gadget>().SingleAsync(g => g.Id == gadget.Id)).Price = 12);
        await StepAsync("delete", async db => db.Remove(await db.Set<Gadget>().SingleAsync(g => g.Id == other.Id)));

        Golden.Match(_rows.ToString());
    }

    private async Task StepAsync(string name, Func<WebhooksTestDbContext, Task> action)
    {
        await app.WithDbAsync(_setup.AdminUserId, _setup.TenantId, async db =>
        {
            await action(db);
            return await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        var rows = await app.WithDbAsync(null, _setup.TenantId, db => db.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Type.StartsWith(typeof(WebhookEntityChanged).FullName!)).OrderBy(m => m.Id).ToListAsync(TestContext.Current.CancellationToken));
        _rows.Append("## ").AppendLine(name);
        foreach (var line in rows.Where(r => _seen.Add(r.Id)).OrderBy(r => r.Payload, StringComparer.Ordinal).Select(row => Named(JsonSerializer.Serialize(new
            {
                row.Type,
                row.Payload,
                OccurredAt = row.OccurredAt == app.Clock.GetUtcNow() ? "now" : row.OccurredAt.ToString("O"),
                Tenant = row.TenantId == _setup.TenantId ? "tenant" : row.TenantId?.ToString(),
                Actor = row.ActorId == _setup.AdminUserId ? "admin" : row.ActorId?.ToString(),
                row.ImpersonatorId,
                row.CorrelationId,
            }))).Order(StringComparer.Ordinal))
        {
            _rows.AppendLine(line);
        }
    }

    private string Named(string text) => _names.Aggregate(text, (current, name) => current.Replace(name.Key, name.Value, StringComparison.OrdinalIgnoreCase));
}
