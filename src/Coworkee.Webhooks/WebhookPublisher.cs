using System.Text.Json;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Webhooks;

internal sealed class WebhookPublisher(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock) : IWebhookPublisher
{
    public Task PublishAsync(string type, object data, CancellationToken cancellationToken = default)
    {
        db.Add(WebhookOutbox.Message(new WebhookAppEvent(type, JsonSerializer.SerializeToElement(data, JsonSerializerOptions.Web)), currentUser.TenantId, currentUser, clock));
        return Task.CompletedTask;
    }
}
