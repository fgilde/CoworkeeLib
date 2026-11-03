using Coworkee.Contracts.Data;
using Coworkee.Domain;

namespace Coworkee.OData.Transfer;

/// <summary>A saved column mapping of an entity set, shared in the organisation.</summary>
public sealed class ImportProfile : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public required string EntitySet { get; set; }

    public required string Name { get; set; }

    /// <summary>Column header to field, as JSON.</summary>
    public required string Columns { get; set; }

    public ImportMode Mode { get; set; }

    public string? KeyField { get; set; }

    public string? Culture { get; set; }
}
