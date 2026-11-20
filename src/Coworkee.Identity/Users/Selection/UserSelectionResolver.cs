using Coworkee.Application.Events;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.OData;

namespace Coworkee.Identity.Users.Selection;

/// <summary>A <see cref="UserSelection"/> through the Users set as the current user reads it: its permission, organisation, row filters and group selection.</summary>
internal sealed class UserSelectionResolver(IServiceProvider services) : IUserSelectionResolver
{
    public static readonly Error Forbidden = Error.Forbidden("identity.selection_forbidden", "Choosing users requires the right to view users.");

    public async Task<Result<IReadOnlyCollection<Guid>>> ResolveAsync(UserSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!await ODataEntityAccess.IsGrantedAsync(services, typeof(User), cancellationToken))
        {
            return Result<IReadOnlyCollection<Guid>>.Failure(Forbidden);
        }

        var ids = new HashSet<Guid>();
        try
        {
            if (selection.AllMatching)
            {
                ids.UnionWith(await (await ODataEntityAccess.FilteredAsync<User>(services, selection.Filter, cancellationToken)).Select(u => u.Id).ToListAsync(cancellationToken));
            }
        }
        catch (ODataException exception)
        {
            return Result<IReadOnlyCollection<Guid>>.Failure(Error.Validation(nameof(UserSelection.Filter), exception.Message));
        }

        // added by hand: only users the caller sees
        if (selection.Included is { Count: > 0 } included)
        {
            var wanted = included.ToList();
            ids.UnionWith(await (await ODataEntityAccess.VisibleAsync<User>(services, cancellationToken)).Where(u => wanted.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken));
        }

        ids.ExceptWith(selection.Excluded ?? []);
        return Result<IReadOnlyCollection<Guid>>.Success(ids);
    }
}
