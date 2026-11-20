using System.Text;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Realtime.Tests;

/// <summary>
/// The exact realtime messages of a fixed sequence of changes ([Realtime] types, a derived type, soft and hard deletes, IHasRealtimeTopics, a topic
/// mapper with the permission topic), captured before the change scans were merged into one collector: they must stay byte-equal.
/// </summary>
public sealed class ChangeGoldenTests(RealtimeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private readonly StringBuilder _messages = new();

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private RecordingPublisher Recording => (RecordingPublisher)app.App.Services.GetRequiredService<IRealtimePublisher>();

    [Fact]
    public async Task The_realtime_messages_of_creates_updates_soft_and_hard_deletes_own_topics_and_mapped_topics()
    {
        var ticket = new Ticket { Title = "Ticket" };
        var urgent = new UrgentTicket { Title = "Urgent", Priority = 1 };
        var memo = new Memo { Text = "Memo" };
        var board = new Board { Name = "Plan" };
        var crate = new Crate { Label = "C1" };
        await StepAsync("create", db =>
        {
            db.AddRange(ticket, urgent, memo, board, crate);
            return Task.CompletedTask;
        });

        await StepAsync("update", async db =>
        {
            (await db.Set<Ticket>().SingleAsync(t => t.Id == ticket.Id)).Title = "Ticket 2";
            (await db.Set<UrgentTicket>().SingleAsync(t => t.Id == urgent.Id)).Priority = 2;
            (await db.Set<Board>().SingleAsync(b => b.Id == board.Id)).Name = "Plan 2";
            (await db.Set<Crate>().SingleAsync(c => c.Id == crate.Id)).Label = "C2";
        });

        await StepAsync("same value", async db => (await db.Set<Ticket>().SingleAsync(t => t.Id == ticket.Id)).Title = "Ticket 2");
        await StepAsync("soft delete", async db => db.Remove(await db.Set<Memo>().SingleAsync(m => m.Id == memo.Id)));
        await StepAsync("hard delete", async db =>
        {
            db.Remove(await db.Set<Ticket>().SingleAsync(t => t.Id == ticket.Id));
            db.Remove(await db.Set<Crate>().SingleAsync(c => c.Id == crate.Id));
        });

        Golden.Match(_messages.ToString());
    }

    private async Task StepAsync(string name, Func<RealtimeTestDbContext, Task> action)
    {
        var before = Recording.Published.Count;
        await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async db =>
        {
            await action(db);
            return await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        _messages.Append("## ").AppendLine(name);
        foreach (var (topic, type, payload, tenantId) in Recording.Published.Skip(before).Where(p => p.Type != PresenceEvents.Changed))
        {
            _messages.AppendLine($"{(tenantId == _setup.TenantId ? "tenant" : tenantId?.ToString() ?? "-")} | {topic} | {type} | {JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)}");
        }
    }
}
