using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Coworkee.Cli.Spec;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Views;

/// <summary>A drawn sketch of the sign-in page in one of the layouts, in the colors of a preview palette, with the backgrounds and looks of a <see cref="SignInSketch"/>.</summary>
public sealed partial class SignInThumb : UserControl
{
    public static readonly StyledProperty<string> LayoutProperty = AvaloniaProperty.Register<SignInThumb, string>(nameof(Layout), "Slide");

    public static readonly StyledProperty<PreviewViewModel?> PreviewProperty = AvaloniaProperty.Register<SignInThumb, PreviewViewModel?>(nameof(Preview));

    public static readonly StyledProperty<SignInSketch?> SketchProperty = AvaloniaProperty.Register<SignInThumb, SignInSketch?>(nameof(Sketch));

    public static readonly StyledProperty<IBrush?> CardBrushProperty = AvaloniaProperty.Register<SignInThumb, IBrush?>(nameof(CardBrush));

    public static readonly StyledProperty<IBrush?> CardBorderProperty = AvaloniaProperty.Register<SignInThumb, IBrush?>(nameof(CardBorder));

    public static readonly StyledProperty<bool> ShowAuroraProperty = AvaloniaProperty.Register<SignInThumb, bool>(nameof(ShowAurora));

    public static readonly StyledProperty<double> DimProperty = AvaloniaProperty.Register<SignInThumb, double>(nameof(Dim));

    public static readonly StyledProperty<double> TintProperty = AvaloniaProperty.Register<SignInThumb, double>(nameof(Tint));

    public static readonly StyledProperty<bool> MediaRightProperty = AvaloniaProperty.Register<SignInThumb, bool>(nameof(MediaRight));

    public static readonly StyledProperty<double> LogoPixelsProperty = AvaloniaProperty.Register<SignInThumb, double>(nameof(LogoPixels), 28);

    public static readonly StyledProperty<ITransform?> BackTransformProperty = AvaloniaProperty.Register<SignInThumb, ITransform?>(nameof(BackTransform));

    public static readonly StyledProperty<ITransform?> MiddleTransformProperty = AvaloniaProperty.Register<SignInThumb, ITransform?>(nameof(MiddleTransform));

    public SignInThumb()
    {
        InitializeComponent();
        Update();
    }

    public string Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public PreviewViewModel? Preview
    {
        get => GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public SignInSketch? Sketch
    {
        get => GetValue(SketchProperty);
        set => SetValue(SketchProperty, value);
    }

    /// <summary>The card or panel of the form: the theme's surface, or frosted glass.</summary>
    public IBrush? CardBrush => GetValue(CardBrushProperty);

    public IBrush? CardBorder => GetValue(CardBorderProperty);

    public bool ShowAurora => GetValue(ShowAuroraProperty);

    /// <summary>Darkening of the background image, 0 to 0.9, where the layout darkens.</summary>
    public double Dim => GetValue(DimProperty);

    /// <summary>How strongly the theme gradient tints the page while it has no fill of its own.</summary>
    public double Tint => GetValue(TintProperty);

    public bool MediaRight => GetValue(MediaRightProperty);

    public double LogoPixels => GetValue(LogoPixelsProperty);

    /// <summary>The two cards behind the form of Stack.</summary>
    public ITransform? BackTransform => GetValue(BackTransformProperty);

    public ITransform? MiddleTransform => GetValue(MiddleTransformProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LayoutProperty || change.Property == PreviewProperty || change.Property == SketchProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        var offers = SignInLayoutOptions.For(Layout);
        var sketch = Sketch;
        var glass = offers.Surface && sketch?.Glass == true;
        var surface = Preview?.Palette.Surface ?? Colors.White;
        SetValue(CardBrushProperty, glass ? new SolidColorBrush(surface, 0.45) : new SolidColorBrush(surface));
        SetValue(CardBorderProperty, glass ? new SolidColorBrush(Colors.White, 0.6) : Preview?.Lines);
        SetValue(ShowAuroraProperty, offers.Animation && sketch?.Aurora == true);
        SetValue(DimProperty, offers.Dim ? sketch?.Dim ?? 0 : 0);
        SetValue(TintProperty, Layout switch { "Spotlight" => 1, "Card" or "Stack" => 0.14, "Minimal" => 0.06, _ => 0 });
        SetValue(MediaRightProperty, sketch?.MediaRight == true);
        SetValue(LogoPixelsProperty, sketch?.LogoSize switch { "Small" => 18d, "Large" => 40d, _ => 28d });
        var tilt = Math.Clamp(sketch?.Tilt ?? SignInStep.DefaultTilt, 0, 12);
        SetValue(BackTransformProperty, new RotateTransform(tilt));
        SetValue(MiddleTransformProperty, new RotateTransform(-tilt / 2));
    }
}
