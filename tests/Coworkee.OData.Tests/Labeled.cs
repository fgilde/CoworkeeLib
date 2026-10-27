using Coworkee.Domain;

namespace Coworkee.OData.Tests;

/// <summary>
/// A base class two sets share, with members the sets hide differently: boxes hide them, bags show them. Stickers point to the base,
/// so the EDM has it as a base type that declares the members (one table, as EF's table per hierarchy).
/// </summary>
public abstract class Labeled : IMultiTenant
{
    // a public setter: OData's conventions take it as the key, so the base becomes an EDM entity type (Coworkee's Entity has an init-only Id)
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string? Note { get; set; }

    public string? Code { get; set; }

    public List<Sticker> Stickers { get; set; } = [];

    public Guid TenantId { get; set; }
}

public sealed class Box : Labeled;

public sealed class Bag : Labeled;

/// <summary>Searched in its text and through its owner's note (ISearchable); weight and author are for users who see categories.</summary>
public sealed class Sticker : AuditedEntity, IMultiTenant, Nextended.Core.Contracts.ISearchable<Sticker>
{
    // a concatenation and a method call on the protected weight, and a navigation into owners (boxes have row filters)
    public static System.Linq.Expressions.Expression<Func<Sticker, string?>>[] SearchProperties =>
        [s => s.Text, s => s.Text + "/" + s.Weight, s => s.Weight.ToString(), s => s.Owner!.Note];

    public required string Text { get; set; }

    public int Weight { get; set; }

    public Guid? OwnerId { get; set; }

    public Labeled? Owner { get; set; }

    public Guid TenantId { get; set; }
}

internal sealed class HideSecretBoxes : IODataEntityFilter<Box>
{
    public Task<IQueryable<Box>> ApplyAsync(IQueryable<Box> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(b => b.Name != "Secret"));
}
