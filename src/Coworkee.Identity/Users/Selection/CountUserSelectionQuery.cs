using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Events;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users.Selection;

[RequiresPermission(IdentityPermissions.Users.View)]
[Description("Counts the users a selection (filter of the Users set, users added and left out) reaches, and how many of them are active.")]
public sealed record CountUserSelectionQuery(UserSelection Selection) : IQuery<Result<UserSelectionCountDto>>;

internal sealed class CountUserSelectionValidator : AbstractValidator<CountUserSelectionQuery>
{
    public CountUserSelectionValidator() => RuleFor(q => q.Selection).NotNull().SetValidator(new UserSelectionValidator());
}

internal sealed class CountUserSelectionHandler(IUserSelectionResolver selections, CoworkeeDbContext db) : IHandler<CountUserSelectionQuery, Result<UserSelectionCountDto>>
{
    public async Task<Result<UserSelectionCountDto>> HandleAsync(CountUserSelectionQuery query, CancellationToken cancellationToken)
    {
        var resolved = await selections.ResolveAsync(query.Selection, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.Error!;
        }

        // the ids came through the Users set: the organisation's own users; active ones are what an event target reaches
        var ids = resolved.Value.ToList();
        var active = ids.Count == 0 ? 0 : await db.Set<User>().AsNoTracking().CountAsync(u => ids.Contains(u.Id) && u.IsActive, cancellationToken);
        return new UserSelectionCountDto(ids.Count, active);
    }
}
