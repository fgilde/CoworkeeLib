using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Tests;

public sealed record DocumentRenamed(Guid DocumentId, string Title) : IDomainEvent;

public sealed class Document : AggregateRoot, IAuditable, ISoftDelete, IMultiTenant, IHasConcurrencyToken
{
    public required string Title { get; set; }

    [Sensitive]
    public string? Secret { get; set; }

    public string? Note { get; set; }

    public string? Internal { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public Guid TenantId { get; set; }

    public uint Version { get; set; }

    public DocumentSettings Settings { get; set; } = new();

    public void Rename(string title)
    {
        Title = title;
        Raise(new DocumentRenamed(Id, title));
    }
}

public sealed class DocumentSettings
{
    public string Color { get; set; } = "none";
}

public sealed class TestModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<Document>();
        document.OwnsOne(d => d.Settings, s => s.ToJson());
        document.Property(d => d.Note).IsSensitive();
        document.Property(d => d.Internal).IsNotAudited();
    }
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors)
{
    public DbSet<Document> Documents => Set<Document>();
}
