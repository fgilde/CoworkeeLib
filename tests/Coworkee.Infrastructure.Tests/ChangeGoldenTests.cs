using System.Text;
using System.Text.Json;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

/// <summary>
/// The exact audit trail of a fixed sequence of changes, captured before the change scans were merged into one collector: it must stay byte-equal.
/// </summary>
public sealed class ChangeGoldenTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new() { ImpersonatorId = Guid.CreateVersion7() };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly HashSet<Guid> _seen = [];
    private readonly StringBuilder _trail = new();

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task The_audit_trail_of_creates_updates_owned_changes_soft_deletes_restores_and_hard_deletes()
    {
        var document = new Document { Title = "Doc", Secret = "s3cret", Note = "note", Internal = "internal" };
        var article = new Article { Title = "Article", Secret = "x" };
        await StepAsync("create", db =>
        {
            db.AddRange(document, article, new Ballot { Answer = "yes" }, new Draft { Name = "draft" });
            return Task.CompletedTask;
        });

        await StepAsync("update", async db =>
        {
            var doc = await db.Documents.SingleAsync(d => d.Id == document.Id);
            doc.Title = "Doc 2";
            doc.Secret = "other";
            doc.Settings.Color = "red";
            doc.Internal = "changed";
            (await db.Set<Article>().SingleAsync(a => a.Id == article.Id)).Title = "Article 2";
        });

        await StepAsync("owned only", async db => (await db.Documents.SingleAsync(d => d.Id == document.Id)).Settings.Color = "blue");
        await StepAsync("not audited property only", async db => (await db.Documents.SingleAsync(d => d.Id == document.Id)).Internal = "again");
        await StepAsync("same value", async db => (await db.Documents.SingleAsync(d => d.Id == document.Id)).Title = "Doc 2");
        await StepAsync("soft delete", async db => db.Remove(await db.Documents.SingleAsync(d => d.Id == document.Id)));
        await StepAsync("restore", async db => (await db.Documents.IgnoreQueryFilters().SingleAsync(d => d.Id == document.Id)).IsDeleted = false);
        await StepAsync("hard delete", async db => db.Remove(await db.Set<Article>().SingleAsync(a => a.Id == article.Id)));

        Golden.Match(_trail.ToString());
    }

    private async Task StepAsync(string name, Func<TestDbContext, Task> action)
    {
        _clock.Advance(TimeSpan.FromMinutes(1));
        await using var provider = database.CreateServices(_user, _clock);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await action(db);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = provider.CreateAsyncScope();
        var entries = (await read.ServiceProvider.GetRequiredService<TestDbContext>().Set<AuditEntry>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken))
            .Where(e => _seen.Add(e.Id))
            .OrderBy(e => e.EntityType, StringComparer.Ordinal).ThenBy(e => e.Action)
            .ToList();
        _trail.Append("## ").AppendLine(name);
        foreach (var e in entries)
        {
            _trail.AppendLine(JsonSerializer.Serialize(new
            {
                e.EntityType,
                e.EntityId,
                Action = e.Action.ToString(),
                Actor = e.ActorId == _user.UserId ? "user" : e.ActorId?.ToString(),
                Impersonator = e.ImpersonatorId == _user.ImpersonatorId ? "impersonator" : e.ImpersonatorId?.ToString(),
                Tenant = e.TenantId == _user.TenantId ? "tenant" : e.TenantId?.ToString(),
                e.CorrelationId,
                e.OccurredAt,
                Changes = e.Changes.Select(c => new { c.Property, c.OldValue, c.NewValue }),
            }));
        }
    }
}
