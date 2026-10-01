using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Persistence;

internal sealed class IdentityModelContributor : IModelContributor
{
    private const string Schema = "cw";

    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.ToTable("Tenants", Schema);
            tenant.Property(t => t.Name).HasMaxLength(200);
            tenant.Property(t => t.Identifier).HasMaxLength(100);
            tenant.HasIndex(t => t.Identifier).IsUnique();
        });

        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users", Schema);
            user.HasKey(u => u.Id);
            user.HasIndex(u => u.NormalizedUserName).IsUnique();
            user.HasIndex(u => u.NormalizedEmail);
            user.HasIndex(u => u.TenantId);
            user.Property(u => u.UserName).HasMaxLength(256);
            user.Property(u => u.NormalizedUserName).HasMaxLength(256);
            user.Property(u => u.Email).HasMaxLength(256);
            user.Property(u => u.NormalizedEmail).HasMaxLength(256);
            user.Property(u => u.FirstName).HasMaxLength(100);
            user.Property(u => u.LastName).HasMaxLength(100);
            user.Property(u => u.PasswordHash).IsSensitive();
            user.Property(u => u.SecurityStamp).IsNotAudited();
            user.Property(u => u.ConcurrencyStamp).IsConcurrencyToken().IsNotAudited();
            user.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId);
            user.HasMany<IdentityUserClaim<Guid>>().WithOne().HasForeignKey(c => c.UserId).IsRequired();
            user.HasMany<IdentityUserLogin<Guid>>().WithOne().HasForeignKey(l => l.UserId).IsRequired();
            user.HasMany<IdentityUserToken<Guid>>().WithOne().HasForeignKey(t => t.UserId).IsRequired();
            user.HasMany<IdentityUserRole<Guid>>().WithOne().HasForeignKey(r => r.UserId).IsRequired();
        });

        modelBuilder.Entity<Role>(role =>
        {
            role.ToTable("Roles", Schema);
            role.HasKey(r => r.Id);
            role.HasIndex(r => new { r.TenantId, r.NormalizedName }).IsUnique().AreNullsDistinct(false);
            role.Property(r => r.Name).HasMaxLength(256);
            role.Property(r => r.NormalizedName).HasMaxLength(256);
            role.Property(r => r.Description).HasMaxLength(1000);
            role.Property(r => r.ConcurrencyStamp).IsConcurrencyToken().IsNotAudited();
            role.HasMany<IdentityUserRole<Guid>>().WithOne().HasForeignKey(r => r.RoleId).IsRequired();
            role.HasMany<IdentityRoleClaim<Guid>>().WithOne().HasForeignKey(c => c.RoleId).IsRequired();
        });

        modelBuilder.Entity<IdentityUserRole<Guid>>(b => { b.ToTable("UserRoles", Schema); b.HasKey(r => new { r.UserId, r.RoleId }); });
        modelBuilder.Entity<IdentityUserClaim<Guid>>(b => { b.ToTable("UserClaims", Schema); b.HasKey(c => c.Id); });
        modelBuilder.Entity<IdentityRoleClaim<Guid>>(b => { b.ToTable("RoleClaims", Schema); b.HasKey(c => c.Id); });
        modelBuilder.Entity<IdentityUserLogin<Guid>>(b =>
        {
            b.ToTable("UserLogins", Schema);
            b.HasKey(l => new { l.LoginProvider, l.ProviderKey });
            b.Property(l => l.LoginProvider).HasMaxLength(128);
            b.Property(l => l.ProviderKey).HasMaxLength(128);
        });
        modelBuilder.Entity<IdentityUserToken<Guid>>(b =>
        {
            b.ToTable("UserTokens", Schema);
            b.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });
            b.Property(t => t.LoginProvider).HasMaxLength(128);
            b.Property(t => t.Name).HasMaxLength(128);
            b.Property(t => t.Value).IsSensitive();
        });

        modelBuilder.Entity<UserGroup>(group =>
        {
            group.ToTable("Groups", Schema);
            group.Property(g => g.Name).HasMaxLength(200);
            group.Property(g => g.Description).HasMaxLength(1000);
            group.HasIndex(g => new { g.TenantId, g.Name }).IsUnique();
            group.HasMany(g => g.Members).WithOne().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
            group.HasMany(g => g.Roles).WithOne().HasForeignKey(r => r.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserGroupMember>(member =>
        {
            member.ToTable("GroupMembers", Schema);
            member.HasKey(m => new { m.GroupId, m.UserId });
            member.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserGroupRole>(groupRole =>
        {
            groupRole.ToTable("GroupRoles", Schema);
            groupRole.HasKey(r => new { r.GroupId, r.RoleId });
            groupRole.HasOne<Role>().WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PermissionGrant>(grant =>
        {
            grant.ToTable("PermissionGrants", Schema);
            grant.Property(g => g.Name).HasMaxLength(200);
            grant.Property(g => g.ProviderType).HasConversion<string>().HasMaxLength(20);
            grant.HasIndex(g => new { g.TenantId, g.Name, g.ProviderType, g.ProviderKey }).IsUnique().AreNullsDistinct(false);
            grant.HasIndex(g => new { g.ProviderType, g.ProviderKey });
        });

        modelBuilder.Entity<ResourcePermission>(permission =>
        {
            permission.ToTable("ResourcePermissions", Schema);
            permission.Property(p => p.ResourceType).HasMaxLength(100);
            permission.Property(p => p.PrincipalType).HasConversion<string>().HasMaxLength(20);
            permission.HasIndex(p => new { p.ResourceType, p.ResourceId, p.PrincipalType, p.PrincipalId, p.RoleId }).IsUnique();
            permission.HasIndex(p => new { p.PrincipalType, p.PrincipalId });
            permission.HasOne<Role>().WithMany().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemState>(state =>
        {
            state.ToTable("SystemState", Schema);
            state.HasKey(s => s.Id);
            state.Property(s => s.Id).ValueGeneratedNever();
        });
    }
}
