using Nextended.Core.Facets;

namespace Coworkee.OData.Tests;

/// <summary>A set keyed by a text, not by a Guid Id.</summary>
public sealed class Country
{
    public required string IsoCode { get; set; }

    public required string Name { get; set; }

    [ProvideFacet(Label = "Continent")]
    public required string Continent { get; set; }
}

/// <summary>A set keyed by a number.</summary>
public sealed class Rank
{
    public int Number { get; set; }

    public required string Title { get; set; }
}

/// <summary>A set keyed by a Guid not named Id; an import updates its rows by that key.</summary>
public sealed class Coupon : Coworkee.Domain.IMultiTenant
{
    public Guid Code { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public int Percent { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class CouponRow
{
    public string Name { get; set; } = string.Empty;

    public int Percent { get; set; }
}

public sealed record SaveCouponCommand(Guid? Code, CouponRow Row) : Coworkee.Application.Messaging.ICommand<Coworkee.Core.Results.Result<Guid>>;

internal sealed class SaveCouponHandler(GadgetDbContext db, Coworkee.Core.Security.ICurrentUser user)
    : Coworkee.Application.Messaging.IHandler<SaveCouponCommand, Coworkee.Core.Results.Result<Guid>>
{
    public async Task<Coworkee.Core.Results.Result<Guid>> HandleAsync(SaveCouponCommand request, CancellationToken cancellationToken)
    {
        var coupon = request.Code is { } code
            ? await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Set<Coupon>(), c => c.Code == code, cancellationToken)
            : db.Add(new Coupon { Name = request.Row.Name, TenantId = user.TenantId!.Value }).Entity;
        (coupon.Name, coupon.Percent) = (request.Row.Name, request.Row.Percent);
        return coupon.Code;
    }
}
