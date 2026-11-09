using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Auditing.Requests;

internal sealed class RequestLogModel : IModelContributor
{
    public const int PathLength = 400;

    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<RequestLogEntry>(entity =>
        {
            entity.ToTable("RequestLog", "cw");
            entity.Property(e => e.ClientId).HasMaxLength(200);
            entity.Property(e => e.Method).HasMaxLength(16);
            entity.Property(e => e.Path).HasMaxLength(PathLength);
            entity.Property(e => e.QueryString).HasMaxLength(2000);
            entity.Property(e => e.Route).HasMaxLength(300);
            entity.Property(e => e.RequestType).HasMaxLength(200);
            entity.Property(e => e.Ip).HasMaxLength(64);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.Property(e => e.TraceId).HasMaxLength(64);
            entity.Property(e => e.Exception).HasMaxLength(2000);
            entity.HasIndex(e => new { e.TenantId, e.At });
            entity.HasIndex(e => new { e.UserId, e.At });
            entity.HasIndex(e => e.Path);
            entity.HasIndex(e => e.At);
        });
}
