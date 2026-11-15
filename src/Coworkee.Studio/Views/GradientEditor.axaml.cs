using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Views;

/// <summary>
/// Edits a <see cref="GradientViewModel"/>: a click on the bar adds a stop in the color the gradient has there, a handle drags its stop,
/// the row below edits the selected stop.
/// </summary>
public sealed partial class GradientEditor : UserControl
{
    private GradientStopViewModel? _dragging;

    public GradientEditor() => InitializeComponent();

    private GradientViewModel? Gradient => DataContext as GradientViewModel;

    private void OnBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Gradient?.AddStop(PositionOf(e)) is { } stop)
        {
            StartDrag(stop, e);
        }
    }

    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is GradientStopViewModel stop && Gradient is { } gradient)
        {
            gradient.Select(stop);
            StartDrag(stop, e);
        }
    }

    private void OnTrackMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is { } stop)
        {
            stop.Position = (int)Math.Round(PositionOf(e));
        }
    }

    private void OnTrackReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = null;
        e.Pointer.Capture(null);
    }

    private void StartDrag(GradientStopViewModel stop, PointerPressedEventArgs e)
    {
        _dragging = stop;
        e.Pointer.Capture(Track);
        e.Handled = true;
    }

    private double PositionOf(PointerEventArgs e) =>
        Bar.Bounds.Width <= 0 ? 0 : Math.Clamp(e.GetPosition(Bar).X / Bar.Bounds.Width * 100, 0, 100);
}

/// <summary>Places each child centered at its <see cref="PositionProperty"/> (0 to 100 percent of the width), for the stop handles.</summary>
public sealed class StopPanel : Panel
{
    public static readonly AttachedProperty<double> PositionProperty = AvaloniaProperty.RegisterAttached<StopPanel, Control, double>("Position");

    static StopPanel() => AffectsParentArrange<StopPanel>(PositionProperty);

    public static double GetPosition(Control control) => control.GetValue(PositionProperty);

    public static void SetPosition(Control control, double value) => control.SetValue(PositionProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0d;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var width = child.DesiredSize.Width;
            child.Arrange(new Rect((finalSize.Width * GetPosition(child) / 100) - (width / 2), 0, width, child.DesiredSize.Height));
        }

        return finalSize;
    }
}
