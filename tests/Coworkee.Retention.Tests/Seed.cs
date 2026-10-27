using System.Text;
using Coworkee.Ai;
using Coworkee.Ai.Sessions;
using Coworkee.Backup;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Webhooks;
using Coworkee.EventBus;
using Coworkee.Files;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Versioning;
using Coworkee.Mailing;
using Coworkee.Notifications;
using Coworkee.OData.Transfer;
using Coworkee.Storage;
using Coworkee.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.Retention.Tests;

/// <summary>One row of every kind of data at the clock's time, plus rows no retention takes (pending, queued, alive); returns the blob keys it wrote.</summary>
public static class Seed
{
    public static async Task<List<string>> EverythingAsync(RetentionApp app, Guid tenantId, Guid userId, string tag)
    {
        var now = app.Clock.GetUtcNow();
        var fileBlob = $"tests/{tag}-file";
        var backupBlob = $"tests/{tag}-backup";
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorage>();
            foreach (var key in new[] { fileBlob, backupBlob })
            {
                await blobs.PutAsync(key, new MemoryStream(Encoding.UTF8.GetBytes(tag)), "text/plain", CancellationToken.None);
            }
        }

        await app.InDbAsync(tenantId, db =>
        {
            db.Add(new AuditEntry { TenantId = tenantId, EntityType = "Gadget", EntityId = tag, OccurredAt = now });
            db.Add(new EntitySnapshot { TenantId = tenantId, EntityType = "Gadget", EntityId = "versioned", Revision = tag == "old" ? 1 : 2, Payload = "{}", CreatedAt = now });
            db.Add(new OutboxMessage { Type = "processed", Payload = "{}", OccurredAt = now, ProcessedAt = now });
            db.Add(new OutboxMessage { Type = "pending", Payload = "{}", OccurredAt = now });
            db.Add(new Gadget { Name = tag, IsDeleted = true, DeletedAt = now });
            db.Add(new Gadget { Name = tag });
            db.Add(new InboxMessage { MessageId = Guid.NewGuid(), Handler = tag, ProcessedAt = now });
            db.Add(new AiToolCall { TenantId = tenantId, UserId = userId, Channel = "assistant", Tool = "list", Input = "{}", Succeeded = true, At = now });
            db.Add(new AiChatSession
            {
                TenantId = tenantId, UserId = userId, Title = tag, CreatedAt = now, UpdatedAt = now,
                Messages = [new AiChatMessage { Role = "user", Text = "hi", At = now }],
            });
            var sent = WebhookDelivery.Queue(tenantId, Guid.NewGuid(), Guid.NewGuid(), "gadgets.created", "{}", now);
            sent.Status = WebhookDeliveryStatus.Succeeded;
            db.Add(sent);
            db.Add(WebhookDelivery.Queue(tenantId, Guid.NewGuid(), Guid.NewGuid(), "gadgets.created", "{}", now));
            var mail = OutgoingMail.Queue("a@acme.test", new RenderedMail("Hi", "<p>Hi</p>"), "test", tenantId, now);
            mail.Status = OutgoingMailStatus.Sent;
            db.Add(mail);
            db.Add(OutgoingMail.Queue("b@acme.test", new RenderedMail("Hi", "<p>Hi</p>"), "test", tenantId, now));
            db.Add(new Notification { UserId = userId, TenantId = tenantId, Type = "test", Title = tag, CreatedAt = now });
            db.Add(new DataTransferJob { TenantId = tenantId, UserId = userId, Kind = DataTransferKind.Export, EntitySet = "Gadgets", Status = DataTransferStatus.Completed, CreatedAt = now, FinishedAt = now });
            db.Add(new DataTransferJob { TenantId = tenantId, UserId = userId, Kind = DataTransferKind.Export, EntitySet = "Gadgets", Status = DataTransferStatus.Running, CreatedAt = now });
            var root = new FileFolder { Name = tag, TenantId = tenantId, IsDeleted = true, DeletedAt = now };
            var child = new FileFolder { Name = "child", ParentId = root.Id, TenantId = tenantId, IsDeleted = true, DeletedAt = now };
            db.AddRange(root, child);
            db.Add(new StoredFile { Name = "a.txt", FolderId = child.Id, ContentType = "text/plain", BlobKey = fileBlob, TenantId = tenantId, IsDeleted = true, DeletedAt = now });
            db.Add(new DatabaseBackup { Name = $"{tag}.zip", BlobKey = backupBlob });
            db.Add(new Infrastructure.Tokens.AccessToken { Hash = $"{tag}{Guid.NewGuid():N}", Purpose = "file", TargetId = Guid.NewGuid(), TenantId = tenantId, ExpiresAt = now });
            db.Add(new OpenIddictEntityFrameworkCoreToken<Guid>
            {
                CreationDate = now.UtcDateTime, ExpirationDate = now.UtcDateTime.AddHours(1), Status = Statuses.Revoked, Type = "access_token", Subject = userId.ToString(),
            });
            return db.SaveChangesAsync();
        });
        return [fileBlob, backupBlob];
    }
}
