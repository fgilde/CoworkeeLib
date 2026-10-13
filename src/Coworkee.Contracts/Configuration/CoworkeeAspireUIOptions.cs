namespace Coworkee.Contracts.Configuration;

/// <summary>The AspireUI whose hosted apps and stacks the service directory lists (<c>Coworkee:AspireUI</c>); the app host fills it while developing.</summary>
public sealed class CoworkeeAspireUIOptions
{
    public const string Section = "Coworkee:AspireUI";

    /// <summary>The address of the AspireUI; without it the source is inactive.</summary>
    public string? Url { get; set; }

    /// <summary>Sent as <c>X-Api-Key</c> when the retrieve API of the AspireUI requires a key.</summary>
    public string? ApiKey { get; set; }
}
