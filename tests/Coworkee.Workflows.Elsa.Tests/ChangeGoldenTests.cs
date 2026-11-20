using System.Text;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Workflows.Elsa.Tests;

/// <summary>
/// The exact outbox rows <see cref="EntityChanged"/> of watched types for a fixed sequence of changes (a soft delete is "Updated" here), captured before
/// the change scans were merged: they must stay byte-equal so running workflows keep their stimulus.
/// </summary>
public sealed class ChangeGoldenTests(ElsaApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private readonly HashSet<Guid> _seen = [];
    private readonly StringBuilder _rows = new();
    private readonly Dictionary<string, string> _names = [];

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task The_outbox_rows_of_watched_entity_types()
    {
        await RecordAsync(null);
        var document = new Document { Title = "Golden", TenantId = _setup.TenantId };
        var memo = new Memo { Text = "Golden", TenantId = _setup.TenantId };
        _names[document.Id.ToString()] = "<document>";
        _names[memo.Id.ToString()] = "<memo>";
        await StepAsync("create", db =>
        {
            db.AddRange(document, memo);
            return Task.CompletedTask;
        });
        await StepAsync("update", async db => (await db.Set<Document>().SingleAsync(d => d.Id == document.Id)).Title = "Golden 2");
        await StepAsync("same value", async db => (await db.Set<Document>().SingleAsync(d => d.Id == document.Id)).Title = "Golden 2");
        await StepAsync("soft delete", async db => db.Remove(await db.Set<Memo>().SingleAsync(m => m.Id == memo.Id)));
        await StepAsync("hard delete", async db => db.Remove(await db.Set<Document>().SingleAsync(d => d.Id == document.Id)));

        Golden.Match(_rows.ToString());
    }

    private async Task StepAsync(string name, Func<ElsaTestDbContext, Task> action)
    {
        await app.InDbAsync(_setup.AdminUserId, _setup.TenantId, async db =>
        {
            await action(db);
            return await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
        await RecordAsync(name);
    }

    // the rows of earlier tests are seen first and left out
    private async Task RecordAsync(string? name)
    {
        var rows = await app.InDbAsync(null, null, db => db.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Type.StartsWith(typeof(EntityChanged).FullName!)).ToListAsync(TestContext.Current.CancellationToken));
        var fresh = rows.Where(r => _seen.Add(r.Id)).OrderBy(r => r.Payload, StringComparer.Ordinal).ToList();
        if (name is null)
        {
            return;
        }

        _rows.Append("## ").AppendLine(name);
        foreach (var line in fresh.Select(row => Named(JsonSerializer.Serialize(new
            {
                row.Type,
                row.Payload,
                OccurredAt = (DateTimeOffset.UtcNow - row.OccurredAt).Duration() < TimeSpan.FromMinutes(5) ? "now" : row.OccurredAt.ToString("O"),
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
