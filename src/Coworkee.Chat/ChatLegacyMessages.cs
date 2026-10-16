using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.Chat;

/// <summary>
/// At start moves direct messages from before conversations existed (no conversation, a recipient) into the direct chat of their two people;
/// what the recipient had read becomes his read mark. Does nothing once they are moved.
/// </summary>
internal sealed class ChatLegacyMessages(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ChatLegacyMessages> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await MoveAsync(scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>(), clock, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // the database may not exist yet (tests, a first start before the migrations); the next start tries again
            logger.LogDebug(exception, "Chat messages from before conversations were not moved");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task MoveAsync(CoworkeeDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var legacy = await db.Set<ChatMessage>().IgnoreQueryFilters().Where(m => m.ConversationId == Guid.Empty && m.ToUserId != null)
            .Select(m => new { m.Id, m.TenantId, m.FromUserId, ToUserId = m.ToUserId!.Value, m.SentAt, m.ReadAt }).ToListAsync(cancellationToken);
        foreach (var pair in legacy.GroupBy(m => (m.TenantId, Key: ChatConversation.KeyOf(m.FromUserId, m.ToUserId))))
        {
            var conversation = await db.Set<ChatConversation>().IgnoreQueryFilters()
                .SingleOrDefaultAsync(c => c.TenantId == pair.Key.TenantId && c.DirectKey == pair.Key.Key, cancellationToken);
            if (conversation is null)
            {
                var first = pair.MinBy(m => m.SentAt)!;
                conversation = new ChatConversation { TenantId = pair.Key.TenantId, DirectKey = pair.Key.Key, CreatedBy = first.FromUserId, CreatedAt = first.SentAt };
                db.Add(conversation);
                foreach (var userId in new[] { first.FromUserId, first.ToUserId })
                {
                    var read = pair.Where(m => m.ToUserId == userId && m.ReadAt != null).Select(m => (DateTimeOffset?)m.SentAt).Max();
                    var sent = pair.Where(m => m.FromUserId == userId).Select(m => (DateTimeOffset?)m.SentAt).Max();
                    db.Add(new ChatMember
                    {
                        TenantId = pair.Key.TenantId, ConversationId = conversation.Id, UserId = userId, JoinedAt = first.SentAt,
                        LastReadAt = new[] { read, sent }.Max() ?? clock.GetUtcNow(),
                    });
                }
            }

            conversation.LastMessageAt = new[] { conversation.LastMessageAt, pair.Max(m => m.SentAt) }.Max();
            await db.SaveChangesAsync(cancellationToken);
            var ids = pair.Select(m => m.Id).ToList();
            await db.Set<ChatMessage>().IgnoreQueryFilters().Where(m => ids.Contains(m.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.ConversationId, conversation.Id), cancellationToken);
        }
    }
}
