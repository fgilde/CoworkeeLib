using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Features;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Features;

[Coworkee.Application.Messaging.AiTool(Exclude = true)]
[RequiresPermission(FeaturePermissions.Tenants)]
public sealed record CreateTenant(CreateTenantRequest Tenant) : ICommand<Result<Guid>>;

[RequiresPermission(FeaturePermissions.Tenants)]
public sealed record UpdateTenant(Guid Id, TenantRequest Tenant) : ICommand<Result>;

[RequiresPermission(FeaturePermissions.Tenants)]
public sealed record GetTenantDetails(IReadOnlyList<Guid> Ids) : IQuery<Result<IReadOnlyList<TenantDetailsDto>>>;

[RequiresPermission(FeaturePermissions.Tenants)]
public sealed record SetTenantFeatures(Guid Id, TenantFeaturesRequest Features) : ICommand<Result>;

internal sealed class TenantHandlers(CoworkeeDbContext db, UserManager<User> users, IFeatureDefinitionManager definitions, HostAccess host)
    : IHandler<CreateTenant, Result<Guid>>,
      IHandler<UpdateTenant, Result>,
      IHandler<GetTenantDetails, Result<IReadOnlyList<TenantDetailsDto>>>,
      IHandler<SetTenantFeatures, Result>
{
    private static readonly Error NotFound = Error.NotFound("tenants.not_found", "The tenant does not exist.");

    public async Task<Result<Guid>> HandleAsync(CreateTenant command, CancellationToken cancellationToken)
    {
        var request = command.Tenant;
        var tenant = new Tenant { Name = string.Empty, Identifier = string.Empty };
        if (await ApplyAsync(tenant, new TenantRequest(request.Name, request.Identifier, request.IsActive, request.AcceptsRegistrations), cancellationToken) is { } error)
        {
            return error;
        }

        db.Add(tenant);
        if (string.IsNullOrWhiteSpace(request.AdminEmail))
        {
            return tenant.Id;
        }

        var admin = new User { TenantId = tenant.Id, UserName = request.AdminEmail, Email = request.AdminEmail, EmailConfirmed = true };
        using (CurrentUserScope.Begin(new ImpersonatedUser(null, tenant.Id)))
        {
            var created = await users.CreateAsync(admin, request.AdminPassword ?? string.Empty);
            if (!created.Succeeded)
            {
                return Error.Validation("Admin", string.Join(" ", created.Errors.Select(e => e.Description)));
            }
        }

        var adminRole = await db.Set<Role>().Where(r => r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id).SingleAsync(cancellationToken);
        db.Add(new IdentityUserRole<Guid> { UserId = admin.Id, RoleId = adminRole });
        return tenant.Id;
    }

    public async Task<Result> HandleAsync(UpdateTenant command, CancellationToken cancellationToken)
    {
        if (await db.Set<Tenant>().SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken) is not { } tenant)
        {
            return NotFound;
        }

        if (tenant.IsDefault && !command.Tenant.IsActive)
        {
            return Error.Validation(nameof(TenantRequest.IsActive), "The system organisation cannot be deactivated.");
        }

        return await ApplyAsync(tenant, command.Tenant, cancellationToken) is { } error ? error : Result.Success();
    }

    public async Task<Result<IReadOnlyList<TenantDetailsDto>>> HandleAsync(GetTenantDetails query, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return HostAccess.Forbidden;
        }

        var ids = query.Ids;
        var sets = await db.Set<TenantFeatureSet>().AsNoTracking().Where(s => ids.Contains(s.TenantId)).ToDictionaryAsync(s => s.TenantId, cancellationToken);
        var counts = await db.Set<User>().Where(u => ids.Contains(u.TenantId)).GroupBy(u => u.TenantId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        IReadOnlyList<TenantDetailsDto> details = ids
            .Select(id => new TenantDetailsDto(id, sets.GetValueOrDefault(id)?.EditionId, sets.GetValueOrDefault(id)?.Overrides ?? [], counts.GetValueOrDefault(id)))
            .ToList();
        return Result<IReadOnlyList<TenantDetailsDto>>.Success(details);
    }

    public async Task<Result> HandleAsync(SetTenantFeatures command, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return HostAccess.Forbidden;
        }

        if (!await db.Set<Tenant>().AnyAsync(t => t.Id == command.Id, cancellationToken))
        {
            return NotFound;
        }

        var request = command.Features;
        if (request.EditionId is { } editionId && !await db.Set<Edition>().AnyAsync(e => e.Id == editionId, cancellationToken))
        {
            return Error.Validation(nameof(request.EditionId), "The edition does not exist.");
        }

        if (definitions.Validate(request.Overrides) is { } invalid)
        {
            return invalid;
        }

        if (await db.Set<TenantFeatureSet>().SingleOrDefaultAsync(s => s.TenantId == command.Id, cancellationToken) is not { } set)
        {
            set = new TenantFeatureSet { TenantId = command.Id };
            db.Add(set);
        }

        set.EditionId = request.EditionId;
        set.Overrides = new Dictionary<string, string>(request.Overrides ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        return Result.Success();
    }

    private async Task<Error?> ApplyAsync(Tenant tenant, TenantRequest request, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return HostAccess.Forbidden;
        }

        var name = request.Name?.Trim();
        var identifier = request.Identifier?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            return Error.Validation(nameof(request.Name), "Name is required and must not exceed 200 characters.");
        }

        if (string.IsNullOrEmpty(identifier) || identifier.Length > 100)
        {
            return Error.Validation(nameof(request.Identifier), "Identifier is required and must not exceed 100 characters.");
        }

        if (await db.Set<Tenant>().AnyAsync(t => t.Identifier == identifier && t.Id != tenant.Id, cancellationToken))
        {
            return Error.Conflict("tenants.identifier_taken", "A tenant with this identifier already exists.");
        }

        tenant.Name = name;
        tenant.Identifier = identifier;
        tenant.IsActive = request.IsActive;
        tenant.AcceptsRegistrations = request.AcceptsRegistrations;
        return null;
    }
}
