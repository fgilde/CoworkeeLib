namespace Coworkee.Contracts.Theming;

/// <summary>
/// A background as <see cref="Color"/> or <see cref="Gradient"/> (the gradient wins when both are set). Colors are <c>#rgb</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>,
/// <c>rgb()</c>, <c>rgba()</c>, <c>hsl()</c>, <c>hsla()</c> with numbers only, or a theme color (<see cref="SignInFills.ThemeColors"/>); the pages drop anything else.
/// </summary>
public sealed class SignInFill
{
    public string? Color { get; set; }

    public SignInGradient? Gradient { get; set; }

    public SignInFill Clone() => new() { Color = Color, Gradient = Gradient?.Clone() };
}

/// <summary>A gradient of up to <see cref="SignInFills.MaxStops"/> stops.</summary>
public sealed class SignInGradient
{
    public SignInGradientKind Kind { get; set; }

    /// <summary>Direction of a linear gradient, start of a conic one; 0 to 359 degrees, 0 points up.</summary>
    public int Angle { get; set; } = 135;

    public List<SignInGradientStop> Stops { get; set; } = [];

    /// <summary>The gradient shifts slowly (stands still with reduced motion).</summary>
    public bool Animated { get; set; }

    public SignInGradient Clone() => new() { Kind = Kind, Angle = Angle, Animated = Animated, Stops = [.. Stops.Select(s => new SignInGradientStop { Color = s.Color, Position = s.Position })] };
}

public sealed class SignInGradientStop
{
    public string Color { get; set; } = string.Empty;

    /// <summary>0 to 100 percent along the gradient.</summary>
    public int Position { get; set; }
}
