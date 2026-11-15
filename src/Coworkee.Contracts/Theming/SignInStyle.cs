using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Theming;

/// <summary>The form's surface in the card and spotlight layouts.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SignInSurface>))]
public enum SignInSurface
{
    Solid,

    /// <summary>Frosted glass over the page background.</summary>
    Glass,
}

/// <summary>Motion behind the form.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SignInAnimation>))]
public enum SignInAnimation
{
    None,

    /// <summary>Blurred fields of the page fill's colors (else the theme's) that drift slowly; still with reduced motion.</summary>
    Aurora,
}

/// <summary>Size of the logo in the minimal layout.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SignInLogoSize>))]
public enum SignInLogoSize
{
    Small,
    Medium,
    Large,
}

[JsonConverter(typeof(JsonStringEnumConverter<SignInGradientKind>))]
public enum SignInGradientKind
{
    Linear,
    Radial,
    Conic,
}
