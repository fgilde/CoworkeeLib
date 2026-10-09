using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Features;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Features;

public sealed record GetFeatures : IQuery<Result<IReadOnlyDictionary<string, string?>>>;

public sealed record GetFeatureDefinitions : IQuery<Result<IReadOnlyList<FeatureGroupDto>>>;

[RequiresPermission(FeaturePermissions.Editions)]
public sealed record GetEditions : IQuery<Result<IReadOnlyList<EditionDto>>>;

[RequiresPermission(FeaturePermissions.Editions)]
public sealed record CreateEdition(EditionRequest Edition) : ICommand<Result<EditionDto>>;

[RequiresPermission(FeaturePermissions.Editions)]
public sealed record UpdateEdition(Guid Id, EditionRequest Edition) : ICommand<Result<EditionDto>>;

[RequiresPermission(FeaturePermissions.Editions)]
public sealed record DeleteEdition(Guid Id) : ICommand<Result>;

internal sealed class FeatureHandlers(IFeatureChecker features, IFeatureDefinitionManager definitions)
    : IHandler<GetFeatures, Result<IReadOnlyDictionary<string, string?>>>, IHandler<GetFeatureDefinitions, Result<IReadOnlyList<FeatureGroupDto>>>
{
    public async Task<Result<IReadOnlyDictionary<string, string?>>> HandleAsync(GetFeatures query, CancellationToken cancellationToken) =>
        Result<IReadOnlyDictionary<string, string?>>.Success(await features.GetAllAsync(cancellationToken));

    public Task<Result<IReadOnlyList<FeatureGroupDto>>> HandleAsync(GetFeatureDefinitions query, CancellationToken cancellationToken)
    {
        IReadOnlyList<FeatureGroupDto> groups = definitions.Groups
            .Select(g => new FeatureGroupDto(g.Name, g.DisplayName, definitions.All
                .Where(d => d.Group == g.Name)
                .Select(d => new FeatureDefinitionDto(d.Name, d.DisplayName, d.Description, d.Type, d.DefaultValue))
                .ToList()))
            .ToList();
        return Task.FromResult(Result<IReadOnlyList<FeatureGroupDto>>.Success(groups));
    }
}

internal sealed class EditionHandlers(CoworkeeDbContext db, IFeatureDefinitionManager definitions, HostAccess host)
    : IHandler<GetEditions, Result<IReadOnlyList<EditionDto>>>,
      IHandler<CreateEdition, Result<EditionDto>>,
      IHandler<UpdateEdition, Result<EditionDto>>,
      IHandler<DeleteEdition, Result>
{
    private static readonly Error NotFound = Error.NotFound("editions.not_found", "The edition does not exist.");

    public async Task<Result<IReadOnlyList<EditionDto>>> HandleAsync(GetEditions query, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return HostAccess.Forbidden;
        }

        IReadOnlyList<EditionDto> editions = await db.Set<Edition>().AsNoTracking().OrderBy(e => e.Name)
            .Select(e => new EditionDto(e.Id, e.Name, e.Description, e.Values, db.Set<TenantFeatureSet>().Count(s => s.EditionId == e.Id)))
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<EditionDto>>.Success(editions);
    }

    public async Task<Result<EditionDto>> HandleAsync(CreateEdition command, CancellationToken cancellationToken)
    {
        var edition = new Edition { Name = string.Empty };
        return await SaveAsync(edition, command.Edition, cancellationToken) is { } error ? error : ToDto(edition);
    }

    public async Task<Result<EditionDto>> HandleAsync(UpdateEdition command, CancellationToken cancellationToken)
    {
        if (await db.Set<Edition>().SingleOrDefaultAsync(e => e.Id == command.Id, cancellationToken) is not { } edition)
        {
            return NotFound;
        }

        return await SaveAsync(edition, command.Edition, cancellationToken) is { } error ? error : ToDto(edition);
    }

    public async Task<Result> HandleAsync(DeleteEdition command, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return HostAccess.Forbidden;
        }

        if (await db.Set<Edition>().SingleOrDefaultAsync(e => e.Id == command.Id, cancellationToken) is not { } edition)
        {
            return NotFound;
        }

        db.Remove(edition);
        return Result.Success();
    }

    private async Task<Error?> SaveAsync(Edition edition, EditionRequest request, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return HostAccess.Forbidden;
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
        {
            return Error.Validation(nameof(request.Name), "Name is required and must not exceed 100 characters.");
        }

        if (definitions.Validate(request.Values) is { } invalid)
        {
            return invalid;
        }

        if (await db.Set<Edition>().AnyAsync(e => e.Name == name && e.Id != edition.Id, cancellationToken))
        {
            return Error.Conflict("editions.name_taken", "An edition with this name already exists.");
        }

        edition.Name = name;
        edition.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        edition.Values = new Dictionary<string, string>(request.Values ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        if (db.Entry(edition).State == EntityState.Detached)
        {
            db.Add(edition);
        }

        return null;
    }

    private static EditionDto ToDto(Edition edition) => new(edition.Id, edition.Name, edition.Description, edition.Values, 0);
}
