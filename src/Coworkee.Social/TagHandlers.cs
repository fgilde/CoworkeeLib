using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Social;

internal sealed class TagHandlers(CoworkeeDbContext db, SocialGuard guard, IPermissionChecker permissions, ICurrentUser currentUser)
    : IHandler<GetTagsQuery, Result<IReadOnlyList<TagDto>>>,
      IHandler<GetEntityTagsQuery, Result<EntityTagsDto>>,
      IHandler<GetTaggedEntitiesQuery, Result<IReadOnlyList<Guid>>>,
      IHandler<SetEntityTagsCommand, Result<EntityTagsDto>>,
      IHandler<DeleteTagCommand, Result>
{
    public async Task<Result<IReadOnlyList<TagDto>>> HandleAsync(GetTagsQuery query, CancellationToken cancellationToken)
    {
        if (await guard.CheckAsync(Access(query.EntityType, null, write: false), cancellationToken) is { } error)
        {
            return error;
        }

        var tags = await Set(query.EntityType)
            .Select(t => new TagDto(t.Name, db.Set<EntityTag>().Count(l => l.TagId == t.Id)))
            .ToListAsync(cancellationToken);
        return tags.OrderByDescending(t => t.Count).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<Result<EntityTagsDto>> HandleAsync(GetEntityTagsQuery query, CancellationToken cancellationToken) =>
        await guard.CheckAsync(Access(query.EntityType, query.EntityId, write: false), cancellationToken) is { } error
            ? error
            : await LoadAsync(query.EntityType, query.EntityId, cancellationToken);

    public async Task<Result<IReadOnlyList<Guid>>> HandleAsync(GetTaggedEntitiesQuery query, CancellationToken cancellationToken)
    {
        if (await guard.CheckAsync(Access(query.EntityType, null, write: false), cancellationToken) is { } error)
        {
            return error;
        }

        var tagId = (await FindAsync(query.EntityType, query.Tag, cancellationToken))?.Id;
        return await db.Set<EntityTag>().Where(l => l.TagId == tagId).Select(l => l.EntityId).ToListAsync(cancellationToken);
    }

    public async Task<Result<EntityTagsDto>> HandleAsync(SetEntityTagsCommand command, CancellationToken cancellationToken)
    {
        if (await guard.CheckAsync(Access(command.EntityType, command.EntityId, write: true), cancellationToken) is { } error)
        {
            return error;
        }

        // ponytail: the tag set of a type is loaded whole to match names without case; page it if a type collects thousands of tags
        var known = await Set(command.EntityType).ToListAsync(cancellationToken);
        var wanted = command.Tags.Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => known.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Create(command.EntityType, name))
            .ToList();
        var links = await db.Set<EntityTag>().Where(l => l.EntityId == command.EntityId && known.Select(t => t.Id).Contains(l.TagId)).ToListAsync(cancellationToken);
        db.RemoveRange(links.Where(l => wanted.All(t => t.Id != l.TagId)));
        db.AddRange(wanted.Where(t => links.All(l => l.TagId != t.Id))
            .Select(t => new EntityTag { TenantId = currentUser.TenantId!.Value, TagId = t.Id, EntityId = command.EntityId }));
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAsync(command.EntityType, command.EntityId, cancellationToken);
    }

    public async Task<Result> HandleAsync(DeleteTagCommand command, CancellationToken cancellationToken)
    {
        if (await guard.CheckAsync(Access(command.EntityType, null, write: true), cancellationToken) is { } error)
        {
            return error;
        }

        if (await FindAsync(command.EntityType, command.Tag, cancellationToken) is not { } tag)
        {
            return Error.NotFound("tags.not_found", "The tag does not exist.");
        }

        db.Remove(tag);
        return Result.Success();
    }

    private Tag Create(string entityType, string name)
    {
        var tag = new Tag { TenantId = currentUser.TenantId!.Value, EntityType = entityType, Name = name };
        db.Add(tag);
        return tag;
    }

    private async Task<Tag?> FindAsync(string entityType, string name, CancellationToken cancellationToken) =>
        (await Set(entityType).ToListAsync(cancellationToken)).FirstOrDefault(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private async Task<EntityTagsDto> LoadAsync(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        var names = await (from link in db.Set<EntityTag>()
                           join tag in Set(entityType) on link.TagId equals tag.Id
                           where link.EntityId == entityId
                           orderby tag.Name
                           select tag.Name).ToListAsync(cancellationToken);
        var canEdit = await permissions.IsGrantedAsync(SocialPermissions.Tags.Create, cancellationToken)
            && await guard.AllowsAsync(Access(entityType, entityId, write: true), cancellationToken);
        return new EntityTagsDto(names, canEdit);
    }

    private IQueryable<Tag> Set(string entityType) => db.Set<Tag>().Where(t => t.EntityType == entityType);

    private static SocialAccess Access(string entityType, Guid? entityId, bool write) => new(SocialFeature.Tags, entityType, entityId, write);
}
