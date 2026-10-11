using System.Text.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Infrastructure.Outbox;

public sealed partial class OutboxProcessor<TContext>(IServiceScopeFactory scopes, TimeProvider clock, ILogger<OutboxProcessor<TContext>> logger)
    where TContext : CoworkeeDbContext
{
    public const int MaxAttempts = 5;

    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var messages = await Pending(db, clock.GetUtcNow(), batchSize).ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                var type = Type.GetType(message.Type, throwOnError: true)!;
                var payload = JsonSerializer.Deserialize(message.Payload, type)!;
                using var actor = CurrentUserScope.Begin(new ImpersonatedUser(message.ActorId, message.TenantId));
                await using var handlerScope = scopes.CreateAsyncScope();
                if (payload is IDomainEvent domainEvent)
                {
                    await DomainEventDispatch.DispatchAsync(domainEvent, handlerScope.ServiceProvider, cancellationToken);
                }

                foreach (var relay in handlerScope.ServiceProvider.GetServices<IOutboxRelay>())
                {
                    await relay.RelayAsync(message, payload, cancellationToken);
                }

                message.ProcessedAt = clock.GetUtcNow();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Attempts++;
                message.NextAttemptAt = clock.GetUtcNow().AddSeconds(Math.Pow(2, message.Attempts));
                message.Error = exception.Message;
                LogFailed(exception, message.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages.Count;
    }

    // rows another instance holds are skipped, not waited for
    private static IQueryable<OutboxMessage> Pending(TContext db, DateTimeOffset now, int batchSize) => db.Database.Provider() switch
    {
        DatabaseProviders.SqlServer => db.Set<OutboxMessage>().FromSql($"""
            SELECT TOP ({batchSize}) * FROM cw.OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE ProcessedAt IS NULL AND Attempts < {MaxAttempts} AND (NextAttemptAt IS NULL OR NextAttemptAt <= {now})
            ORDER BY OccurredAt
            """),
        // ponytail: SQLite has one writer at a time and no row locks; several processes may pick the same message, handlers are idempotent through the inbox
        DatabaseProviders.Sqlite => db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.OccurredAt).Take(batchSize),
        _ => db.Set<OutboxMessage>().FromSql($"""
            SELECT * FROM cw."OutboxMessages"
            WHERE "ProcessedAt" IS NULL AND "Attempts" < {MaxAttempts} AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {now})
            ORDER BY "OccurredAt" LIMIT {batchSize} FOR UPDATE SKIP LOCKED
            """),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} failed")]
    private partial void LogFailed(Exception exception, Guid messageId);
}
