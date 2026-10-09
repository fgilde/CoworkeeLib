using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Social;

internal sealed class SocialModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Comment>(comment =>
        {
            comment.ToTable("Comments", "cw");
            comment.Ignore(c => c.RealtimeTopics);
            comment.Property(c => c.EntityType).HasMaxLength(SocialLimits.EntityType);
            comment.Property(c => c.Text).HasMaxLength(SocialLimits.CommentText);
            comment.HasIndex(c => new { c.TenantId, c.EntityType, c.EntityId, c.CreatedAt });
            comment.HasOne<Comment>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Tag>(tag =>
        {
            tag.ToTable("Tags", "cw");
            tag.Property(t => t.EntityType).HasMaxLength(SocialLimits.EntityType);
            tag.Property(t => t.Name).HasMaxLength(SocialLimits.TagName);
            tag.HasIndex(t => new { t.TenantId, t.EntityType, t.Name }).IsUnique();
        });

        modelBuilder.Entity<EntityTag>(link =>
        {
            link.ToTable("EntityTags", "cw");
            link.HasOne<Tag>().WithMany().HasForeignKey(l => l.TagId).OnDelete(DeleteBehavior.Cascade);
            link.HasIndex(l => new { l.TagId, l.EntityId }).IsUnique();
            link.HasIndex(l => new { l.TenantId, l.EntityId });
        });

        modelBuilder.Entity<Rating>(rating =>
        {
            rating.ToTable("Ratings", "cw", table => table.HasCheckConstraint("CK_Ratings_Stars", "\"Stars\" BETWEEN 1 AND 5"));
            rating.Property(r => r.EntityType).HasMaxLength(SocialLimits.EntityType);
            rating.HasIndex(r => new { r.TenantId, r.EntityType, r.EntityId, r.UserId }).IsUnique();
        });
    }
}

internal static class SocialLimits
{
    public const int EntityType = 100;
    public const int CommentText = 4000;
    public const int TagName = 50;
}
