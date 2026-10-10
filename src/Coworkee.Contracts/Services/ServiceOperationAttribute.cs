namespace Coworkee.Contracts.Services;

/// <summary>
/// Overrides the conventions of one method: <see cref="Method"/> like "POST", <see cref="Route"/> relative to the service like "{id}/archive";
/// <see cref="Ignore"/> keeps the method off the API (its proxy throws <see cref="NotSupportedException"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ServiceOperationAttribute : Attribute
{
    public string? Method { get; set; }

    public string? Route { get; set; }

    public bool Ignore { get; set; }
}
