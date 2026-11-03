namespace Coworkee.Features;

/// <summary>The request is only handled while the bool feature is on for the current tenant.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresFeatureAttribute(string feature) : Attribute
{
    public string Feature { get; } = feature;
}
