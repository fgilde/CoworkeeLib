using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Chat;

internal sealed class ChatModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ChatMessage>(message =>
        {
            message.ToTable("ChatMessages", "cw");
            message.Property(m => m.Text).HasMaxLength(ChatLimits.TextLength);
            message.HasIndex(m => new { m.TenantId, m.FromUserId, m.ToUserId, m.SentAt });
            message.HasIndex(m => new { m.TenantId, m.ToUserId, m.ReadAt });
        });
}

internal static class ChatLimits
{
    public const int TextLength = 4000;
}
