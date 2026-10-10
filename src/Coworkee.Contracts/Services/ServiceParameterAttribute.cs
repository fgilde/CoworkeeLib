namespace Coworkee.Contracts.Services;

/// <summary>Binds a parameter from another part of the request than the convention would, optionally under another <see cref="Name"/>.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ServiceParameterAttribute(ParameterSource source) : Attribute
{
    public ParameterSource Source { get; } = source;

    public string? Name { get; set; }
}

public enum ParameterSource
{
    Route,
    Query,
    Body,
}
