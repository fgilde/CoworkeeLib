using Avalonia.Media;
using Avalonia.Media.Imaging;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

/// <summary>Brushes of one palette for the mini app shell and the sign-in thumbnails.</summary>
public sealed record PreviewViewModel(Palette Palette, double Radius, FontFamily Font, string Title, Bitmap? Logo, bool Dark)
{
    public IBrush Primary { get; } = new SolidColorBrush(Palette.Primary);

    public IBrush PrimaryText { get; } = new SolidColorBrush(Palette.PrimaryText);

    public IBrush Secondary { get; } = new SolidColorBrush(Palette.Secondary);

    public IBrush SecondaryText { get; } = new SolidColorBrush(Palette.SecondaryText);

    public IBrush Background { get; } = new SolidColorBrush(Palette.Background);

    public IBrush Surface { get; } = new SolidColorBrush(Palette.Surface);

    public IBrush AppBar { get; } = new SolidColorBrush(Palette.AppBar);

    public IBrush AppBarText { get; } = new SolidColorBrush(Palette.AppBarText);

    public IBrush Drawer { get; } = new SolidColorBrush(Palette.Drawer);

    public IBrush Text { get; } = new SolidColorBrush(Palette.Text);

    public IBrush TextSecondary { get; } = new SolidColorBrush(Palette.TextSecondary);

    public IBrush Lines { get; } = new SolidColorBrush(Palette.Lines);

    public IBrush Gradient { get; } = new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
        GradientStops = { new GradientStop(Palette.Primary, 0), new GradientStop(Palette.Secondary, 1) },
    };

    public Avalonia.CornerRadius Corner { get; } = new(Radius);

    public Avalonia.CornerRadius SmallCorner { get; } = new(Math.Min(Radius, 8));

    public bool HasLogo => Logo is not null;
}
