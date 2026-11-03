using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Theming;

[JsonConverter(typeof(JsonStringEnumConverter<LoadingScreenBackground>))]
public enum LoadingScreenBackground
{
    /// <summary>Soft moving shapes in the theme's colours.</summary>
    Animated,

    /// <summary>The sign-in page's image or video under a veil in the theme's colours; animated when there is none.</summary>
    SignInMedia,

    /// <summary>The theme's background colour only.</summary>
    Plain,
}
