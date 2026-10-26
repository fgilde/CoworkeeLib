using Coworkee.Application.Groups;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.Tests;

/// <summary>A row of one organisation unit; lists show the rows of the active units (<see cref="CratesOfActiveUnits"/>).</summary>
public sealed class Crate : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public int Quantity { get; set; }

    public Guid UnitId { get; set; }

    public Guid TenantId { get; set; }
}

internal sealed class CratesOfActiveUnits(IGroupScope groups) : IODataEntityFilter<Crate>
{
    public async Task<IQueryable<Crate>> ApplyAsync(IQueryable<Crate> query, CancellationToken cancellationToken)
    {
        var active = (await groups.ActiveAsync(cancellationToken)).ToList();
        return query.Where(c => EF.Parameter(active).Contains(c.UnitId));
    }
}

public sealed class CrateRow
{
    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

public sealed record SaveCrateCommand(Guid? Id, CrateRow Row) : ICommand<Result<Guid>>;

internal sealed class SaveCrateHandler(GadgetDbContext db, ICurrentUser user) : IHandler<SaveCrateCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(SaveCrateCommand request, CancellationToken cancellationToken)
    {
        var crate = request.Id is { } id ? await db.Set<Crate>().SingleAsync(c => c.Id == id, cancellationToken) : null;
        if (crate is null)
        {
            crate = new Crate { Name = request.Row.Name, TenantId = user.TenantId!.Value };
            db.Add(crate);
        }

        (crate.Name, crate.Quantity) = (request.Row.Name, request.Row.Quantity);
        await db.SaveChangesAsync(cancellationToken);
        return crate.Id;
    }
}
