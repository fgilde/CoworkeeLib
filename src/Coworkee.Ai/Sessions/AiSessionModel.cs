using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Ai.Sessions;

internal sealed class AiSessionModel : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiChatSession>(session =>
        {
            session.ToTable("AiChatSessions", "cw");
            session.Property(s => s.Title).HasMaxLength(AiSessionLimits.Title);
            session.HasIndex(s => new { s.TenantId, s.UserId, s.UpdatedAt });
            session.HasMany(s => s.Messages).WithOne().HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AiChatMessage>(message =>
        {
            message.ToTable("AiChatMessages", "cw");
            message.Property(m => m.Role).HasMaxLength(20);
            message.Property(m => m.Decision).HasMaxLength(20);
            message.HasIndex(m => new { m.SessionId, m.At });
        });
    }
}

internal static class AiSessionLimits
{
    public const int Title = 200;

    public const int Sessions = 200;
}
