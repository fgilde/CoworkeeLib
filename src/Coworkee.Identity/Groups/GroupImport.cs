using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

/// <summary>A row of the group import: <c>ParentPath</c> is the slash-joined names of the parents from the root ("Customer A/Hamburg"), empty for a root group.</summary>
public sealed record GroupImportRow(string Name, string? Description, string? ParentPath);

/// <summary>Creates the group at its path or, when a group of that name already sits there, updates its description.</summary>
[RequiresPermission(IdentityPermissions.Groups.Manage)]
[AiTool(Exclude = true)]
public sealed record ImportGroup(GroupImportRow Row) : ICommand<Result<Guid>>;

// ponytail: the path is only resolved by the handler, so a dry run accepts rows whose parent an earlier row of the same file creates and cannot report an unknown path;
// names containing '/' cannot be addressed by path (rejected here), empty path segments are ignored, a blank description does not clear an existing one
internal sealed class ImportGroupValidator : AbstractValidator<ImportGroup>
{
    public ImportGroupValidator() => RuleFor(c => c.Row.Name).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(200).Must(name => !name.Contains('/')).WithMessage("A group name cannot contain '/'.");
}

internal sealed class ImportGroupHandler(CoworkeeDbContext db, Users.AdminReach reach) : IHandler<ImportGroup, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(ImportGroup command, CancellationToken cancellationToken)
    {
        var groups = db.Set<UserGroup>();
        Guid? parentId = null;
        foreach (var segment in (command.Row.ParentPath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var current = parentId;
            parentId = await groups.Where(g => g.ParentId == current && g.Name == segment).Select(g => (Guid?)g.Id).SingleOrDefaultAsync(cancellationToken);
            if (parentId is null)
            {
                return GroupErrors.ParentPathUnknown;
            }
        }

        // a group-scoped administrator imports below the groups of his reach; a parent out of it is as unknown as a missing one (paths start at the roots)
        if (await reach.CheckParentAsync(parentId, cancellationToken) is { } refused)
        {
            return parentId is null ? refused : GroupErrors.ParentPathUnknown;
        }

        var name = command.Row.Name.Trim();
        var existing = await groups.SingleOrDefaultAsync(g => g.ParentId == parentId && g.Name == name, cancellationToken);
        if (existing is null)
        {
            return await CreateGroupHandler.AddAsync(db, new GroupRequest(name, command.Row.Description?.Trim(), parentId), cancellationToken);
        }

        // the default group below a group in reach
        if (!await reach.ReachesGroupAsync(existing.Id, cancellationToken))
        {
            return Users.AdminReach.AllGroupsRequired;
        }

        if (!string.IsNullOrWhiteSpace(command.Row.Description))
        {
            existing.Description = command.Row.Description.Trim();
        }

        return existing.Id;
    }
}
