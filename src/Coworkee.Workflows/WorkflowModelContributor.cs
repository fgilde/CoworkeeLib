using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Workflows;

internal sealed class WorkflowModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowItem>(item =>
        {
            item.ToTable("WorkflowItems", "cw");
            item.Ignore(i => i.RealtimeTopics);
            item.Property(i => i.EntityType).HasMaxLength(WorkflowLimits.EntityType);
            item.Property(i => i.State).HasMaxLength(WorkflowLimits.Name);
            item.HasIndex(i => new { i.TenantId, i.EntityType, i.EntityId }).IsUnique();
            item.HasIndex(i => new { i.TenantId, i.EntityType, i.State });
        });

        modelBuilder.Entity<WorkflowStep>(step =>
        {
            step.ToTable("WorkflowSteps", "cw");
            step.Property(s => s.EntityType).HasMaxLength(WorkflowLimits.EntityType);
            step.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
            step.Property(s => s.From).HasMaxLength(WorkflowLimits.Name);
            step.Property(s => s.To).HasMaxLength(WorkflowLimits.Name);
            step.Property(s => s.Transition).HasMaxLength(WorkflowLimits.Name);
            step.Property(s => s.Comment).HasMaxLength(WorkflowLimits.Comment);
            step.HasIndex(s => new { s.TenantId, s.EntityType, s.EntityId, s.At });
        });

        modelBuilder.Entity<WorkflowApproval>(approval =>
        {
            approval.ToTable("WorkflowApprovals", "cw");
            approval.Property(a => a.EntityType).HasMaxLength(WorkflowLimits.EntityType);
            approval.Property(a => a.State).HasMaxLength(WorkflowLimits.Name);
            approval.Property(a => a.Title).HasMaxLength(WorkflowLimits.Title);
            approval.Property(a => a.Link).HasMaxLength(WorkflowLimits.Link);
            approval.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            approval.HasMany(a => a.Approvers).WithOne().HasForeignKey(a => a.ApprovalId).OnDelete(DeleteBehavior.Cascade);
            approval.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId, a.Status });
            approval.HasIndex(a => new { a.Status, a.DueAt });
        });

        modelBuilder.Entity<WorkflowApprover>(approver =>
        {
            approver.ToTable("WorkflowApprovers", "cw");
            approver.Ignore(a => a.RealtimeTopics);
            approver.Property(a => a.Comment).HasMaxLength(WorkflowLimits.Comment);
            approver.HasIndex(a => new { a.UserId, a.Approved });
        });
    }
}
