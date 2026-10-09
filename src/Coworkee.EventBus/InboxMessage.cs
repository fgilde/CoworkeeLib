using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.EventBus;

/// <summary>A message a handler already processed; redeliveries of it are skipped.</summary>
[NotAudited]
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }

    public required string Handler { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}

internal sealed class InboxModelContributor : IModelContributor
{
    // ponytail: inbox rows are kept forever, add a retention cleanup when the table grows too large
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<InboxMessage>(entity =>
    {
        entity.ToTable("InboxMessages", "cw");
        entity.HasKey(e => new { e.MessageId, e.Handler });
        entity.Property(e => e.Handler).HasMaxLength(512);
    });
}
