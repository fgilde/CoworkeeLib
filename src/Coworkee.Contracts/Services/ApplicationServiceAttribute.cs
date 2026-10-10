namespace Coworkee.Contracts.Services;

/// <summary>Overrides the conventions of a service: <see cref="Route"/> like "/api/v2/invoices", <see cref="Tag"/> for the OpenAPI group.</summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class ApplicationServiceAttribute : Attribute
{
    public string? Route { get; set; }

    public string? Tag { get; set; }
}
