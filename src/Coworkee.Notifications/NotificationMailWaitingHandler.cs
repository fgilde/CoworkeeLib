using Coworkee.Application.Messaging;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Notifications;

/// <summary>Adds the waiting mail or replaces the one of the same user, type and entity (debounce); each try in a context of its own, the outbox retries.</summary>
internal sealed partial class NotificationMailWaitingHandler(IServiceScopeFactory scopes, ILogger<NotificationMailWaitingHandler> logger) : IDomainEventHandler<NotificationMailWaiting>
{
    private const int Attempts = 3;

    public async Task HandleAsync(NotificationMailWaiting waiting, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
            var mail = await db.Set<PendingNotificationMail>()
                .FirstOrDefaultAsync(m => m.UserId == waiting.UserId && m.Type == waiting.Type && m.Key == waiting.Key, cancellationToken);
            if (mail is null)
            {
                mail = new PendingNotificationMail { UserId = waiting.UserId, TenantId = waiting.TenantId, Type = waiting.Type, Key = waiting.Key, Title = waiting.Title };
                db.Add(mail);
            }
            else if (waiting.DueAt < mail.DueAt)
            {
                // an older notification, retried by the outbox after a newer one: the newer text stays
                return;
            }

            (mail.Title, mail.Body, mail.Link, mail.MailModel, mail.DueAt, mail.Version) = (waiting.Title, waiting.Body, waiting.Link, waiting.MailModel, waiting.DueAt, Guid.NewGuid());
            mail.Arguments = waiting.Arguments is null ? null : [.. waiting.Arguments];
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception) when (attempt < Attempts)
            {
                // another notification added it, or the mail job took it meanwhile: look again
                LogRetry(exception, waiting.Type, waiting.UserId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The waiting mail of {Type} for {UserId} changed meanwhile; trying again.")]
    private partial void LogRetry(Exception exception, string type, Guid userId);
}
