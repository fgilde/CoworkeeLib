using Coworkee.Domain;

namespace Coworkee.Reporting.Tests;

public enum SaleStatus
{
    Open,
    Shipped,
    Cancelled,
}

public sealed class Sale : Entity, IMultiTenant
{
    public required string Region { get; set; }

    public SaleStatus Status { get; set; }

    public decimal Amount { get; set; }

    public int Quantity { get; set; }

    public DateTimeOffset SoldAt { get; set; }

    public DateOnly? ShippedOn { get; set; }

    public Guid ShopperId { get; set; }

    public Shopper? Shopper { get; set; }

    public string? Secret { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class Shopper : Entity, IMultiTenant
{
    public required string Name { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>A set keyed by a code, not by a Guid Id.</summary>
public sealed class Store : IMultiTenant
{
    public required string Code { get; set; }

    public required string Region { get; set; }

    public int Seats { get; set; }

    /// <summary>Kept as text by its own conversion, not as the convention's number.</summary>
    public DateTimeOffset? OpenedAt { get; set; }

    public Guid TenantId { get; set; }
}
