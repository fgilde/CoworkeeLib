using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

/// <summary>One color of a gradient. <see cref="Text"/> is what the spec stores (a theme color stays one until the color is changed).</summary>
public sealed partial class GradientStopViewModel(string text, int position, Func<string, Color> resolve) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Color), nameof(Brush))]
    public partial string Text { get; set; } = text;

    [ObservableProperty]
    public partial int Position { get; set; } = position;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public Color Color
    {
        get => resolve(Text);
        set => Text = FillBrush.Hex(value);
    }

    public IBrush Brush => new SolidColorBrush(Color);
}

/// <summary>
/// A multi-stop gradient: kind, angle, animation and two to <see cref="SignInStyle.MaxStops"/> stops; a stop is added where the bar is clicked,
/// in the color the gradient has there. <see cref="Changed"/> follows every edit.
/// </summary>
public sealed partial class GradientViewModel : ObservableObject
{
    private readonly Func<string, Color> _resolve;

    public GradientViewModel(SignInGradientSpec spec, Func<string, Color> resolve)
    {
        _resolve = resolve;
        Kind = spec.Kind;
        Angle = Math.Clamp(spec.Angle, 0, 359);
        Animated = spec.Animated;
        Stops = [];
        foreach (var stop in spec.Stops.Take(SignInStyle.MaxStops))
        {
            Add(new GradientStopViewModel(stop.Color, Math.Clamp(stop.Position, 0, 100), resolve));
        }

        Stops.CollectionChanged += (_, _) => Raise();
        Select(Stops.FirstOrDefault());
    }

    public event Action? Changed;

    public static IReadOnlyList<string> Kinds { get; } = [.. Enum.GetNames<GradientKind>()];

    public ObservableCollection<GradientStopViewModel> Stops { get; }

    [ObservableProperty]
    public partial GradientKind Kind { get; set; }

    /// <summary><see cref="Kind"/> as text for the segments.</summary>
    public string KindName
    {
        get => Kind.ToString();
        set => Kind = Enum.TryParse<GradientKind>(value, out var kind) ? kind : Kind;
    }

    [ObservableProperty]
    public partial int Angle { get; set; }

    [ObservableProperty]
    public partial bool Animated { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveStopCommand))]
    public partial GradientStopViewModel? SelectedStop { get; set; }

    public bool HasSelection => SelectedStop is not null;

    /// <summary>Radial gradients have no direction.</summary>
    public bool HasAngle => Kind != GradientKind.Radial;

    public bool CanAdd => Stops.Count < SignInStyle.MaxStops;

    public bool CanRemove => Stops.Count > 2;

    /// <summary>The stops from left to right, as the bar above the handles shows them.</summary>
    public IBrush Bar => FillBrush.Gradient(new SignInGradientSpec { Stops = Spec().Stops }, _resolve);

    /// <summary>The gradient as the page draws it.</summary>
    public IBrush Brush => FillBrush.Gradient(Spec(), _resolve);

    public static SignInGradientSpec Default(string from = "primary", string to = "secondary") =>
        new() { Kind = GradientKind.Linear, Angle = 135, Stops = [new() { Color = from, Position = 0 }, new() { Color = to, Position = 100 }] };

    public SignInGradientSpec Spec() => new()
    {
        Kind = Kind,
        Angle = Angle,
        Animated = Animated,
        Stops = [.. Stops.OrderBy(s => s.Position).Select(s => new SignInGradientStopSpec { Color = s.Text, Position = s.Position })],
    };

    /// <summary>Adds a stop at <paramref name="position"/> in the color the gradient has there and selects it; null when there are already eight.</summary>
    public GradientStopViewModel? AddStop(double position)
    {
        if (!CanAdd)
        {
            return null;
        }

        var at = (int)Math.Round(Math.Clamp(position, 0, 100));
        var stop = new GradientStopViewModel(FillBrush.Hex(FillBrush.At([.. Stops.Select(s => (s.Color, s.Position))], at)), at, _resolve);
        Add(stop);
        Select(stop);
        return stop;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    public void RemoveStop()
    {
        if (SelectedStop is { } stop && CanRemove)
        {
            var index = Stops.IndexOf(stop);
            stop.PropertyChanged -= StopChanged;
            Stops.Remove(stop);
            Select(Stops[Math.Min(index, Stops.Count - 1)]);
        }
    }

    public void Select(GradientStopViewModel? stop)
    {
        foreach (var each in Stops)
        {
            each.IsSelected = each == stop;
        }

        SelectedStop = stop;
    }

    private bool CanRemoveSelected() => SelectedStop is not null && CanRemove;

    private void Add(GradientStopViewModel stop)
    {
        stop.PropertyChanged += StopChanged;
        Stops.Add(stop);
    }

    private void StopChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GradientStopViewModel.Text) or nameof(GradientStopViewModel.Position))
        {
            Raise();
        }
    }

    partial void OnKindChanged(GradientKind value)
    {
        OnPropertyChanged(nameof(KindName));
        OnPropertyChanged(nameof(HasAngle));
        Raise();
    }

    partial void OnAngleChanged(int value) => Raise();

    partial void OnAnimatedChanged(bool value) => Raise();

    private void Raise()
    {
        if (Stops is null)
        {
            return; // the constructor sets kind and angle first
        }

        OnPropertyChanged(nameof(Bar));
        OnPropertyChanged(nameof(Brush));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanRemove));
        RemoveStopCommand.NotifyCanExecuteChanged();
        Changed?.Invoke();
    }
}
