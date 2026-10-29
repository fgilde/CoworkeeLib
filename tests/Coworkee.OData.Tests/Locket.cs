using System.Linq.Expressions;
using Coworkee.Domain;
using Microsoft.OData.ModelBuilder;

namespace Coworkee.OData.Tests;

/// <summary>
/// Members C# only reads, over fields EF maps: <see cref="Note"/> is hidden, <see cref="Code"/> protected. Two rows per page; <c>Peek</c> is a computed
/// property EF cannot translate.
/// </summary>
[Page(PageSize = 2)]
public sealed class Locket : AuditedEntity, IMultiTenant
{
    private string? _note;
    private string? _code;

    public required string Name { get; set; }

    public string? Note => _note;

    public string? Code => _code;

    public Guid TenantId { get; set; }

    public static Locket Create(string name, string note, string code, Guid tenantId) => new() { Name = name, _note = note, _code = code, TenantId = tenantId };
}

/// <summary>A hidden member that is a getter over nothing EF knows: it cannot be cleared on the entity.</summary>
public sealed class Ledger : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public string Summary => "internal " + Name;

    public Guid TenantId { get; set; }
}

internal sealed class LocketPeek : Coworkee.OData.Computed.IODataComputedPropertySource
{
    public static string Peek(Locket locket) => locket.Name;

    public IEnumerable<Coworkee.OData.Computed.ODataComputedProperty> Properties(ODataEntityRegistry registry) =>
        [new(typeof(Locket), "Peek", typeof(string), row => Expression.Call(typeof(LocketPeek).GetMethod(nameof(Peek))!, row))];
}
