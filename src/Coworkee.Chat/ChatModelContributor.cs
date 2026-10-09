using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Chat;

internal sealed class ChatModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ChatMessage>(message =>
        {
            message.ToTable("ChatMessages", "cw");
            // the text stays out of the audit trail, so erasing a user's messages leaves no copy behind
            message.Property(m => m.Text).HasMaxLength(ChatLimits.TextLength).IsNotAudited();
            message.HasIndex(m => new { m.TenantId, m.FromUserId, m.ToUserId, m.SentAt });
            message.HasIndex(m => new { m.TenantId, m.ToUserId, m.ReadAt });
        });
}

internal static class ChatLimits
{
    public const int TextLength = 4000;
}
