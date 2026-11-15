using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor.Utilities;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>
/// Edits a <see cref="SignInGradient"/> in place: a live preview, the stops as handles on a track (press the track to add a stop with the color at that
/// point, drag a handle or use the arrow keys to move it, the stops stay ordered), the selected stop's color, theme color and position, kind, angle,
/// motion and presets. <see cref="ValueChanged"/> follows every change.
/// </summary>
public partial class GradientEditor : IAsyncDisposable
{
    private ElementReference _track;
    private IJSObjectReference? _module;
    private IJSObjectReference? _attached;
    private DotNetObjectReference<GradientEditor>? _self;
    private SignInGradientStop? _selected;
    private bool _attaching;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Parameter, EditorRequired] public SignInGradient? Value { get; set; }

    [Parameter] public EventCallback<SignInGradient> ValueChanged { get; set; }

    /// <summary>A theme color (<see cref="SignInFills.ThemeColors"/>) as CSS of this page, MudBlazor's palette variables unless set.</summary>
    [Parameter] public Func<string, string> ThemeColor { get; set; } = MudThemeColor;

    /// <summary>The theme colors as MudBlazor's palette variables.</summary>
    public static string MudThemeColor(string color) => $"var(--mud-palette-{color})";

    /// <summary>A gradient to start with: the theme's primary to tertiary color.</summary>
    public static SignInGradient Default() => new()
    {
        Angle = 135, Stops = [new() { Color = "primary", Position = 0 }, new() { Color = "tertiary", Position = 100 }],
    };

    /// <summary>The CSS of <see cref="Value"/> as the pages draw it.</summary>
    public string? Css => SignInFills.Css(Value is null ? null : new SignInFill { Gradient = Value }, ThemeColor);

    internal int Selected => _selected is { } stop && Value?.Stops.IndexOf(stop) is >= 0 and var index ? index : Value?.Stops.Count > 0 ? 0 : -1;

    private SignInGradientStop? SelectedStop => Selected >= 0 ? Value!.Stops[Selected] : null;

    // the stops left to right whatever the kind
    private string TrackCss => Value is { Stops.Count: > 0 }
        ? $"linear-gradient(90deg, {string.Join(", ", Value.Stops.OrderBy(s => s.Position).Select(s => $"{Show(s.Color)} {s.Position}%"))})"
        : "none";

    private static readonly (string Name, SignInGradient Gradient)[] Presets =
    [
        ("Theme", Default()),
        ("Theme colors", new() { Angle = 120, Stops = [new() { Color = "primary", Position = 0 }, new() { Color = "secondary", Position = 50 }, new() { Color = "tertiary", Position = 100 }] }),
        ("Sunset", new() { Angle = 160, Stops = [new() { Color = "#ff5f6d", Position = 0 }, new() { Color = "#ffc371", Position = 100 }] }),
        ("Ocean", new() { Angle = 200, Stops = [new() { Color = "#0f2027", Position = 0 }, new() { Color = "#2c5364", Position = 55 }, new() { Color = "#4ca1af", Position = 100 }] }),
        ("Aurora", new() { Angle = 135, Animated = true, Stops = [new() { Color = "#00c9a7", Position = 0 }, new() { Color = "#845ec2", Position = 50 }, new() { Color = "#0081cf", Position = 100 }] }),
        ("Glow", new() { Kind = SignInGradientKind.Radial, Stops = [new() { Color = "primary", Position = 0 }, new() { Color = "background", Position = 70 }] }),
        ("Spectrum", new() { Kind = SignInGradientKind.Conic, Angle = 0, Stops = [new() { Color = "#ff6b6b", Position = 0 }, new() { Color = "#feca57", Position = 25 }, new() { Color = "#48dbfb", Position = 50 }, new() { Color = "#ff9ff3", Position = 75 }, new() { Color = "#ff6b6b", Position = 100 }] }),
        ("Night", new() { Angle = 180, Stops = [new() { Color = "#141e30", Position = 0 }, new() { Color = "#243b55", Position = 100 }] }),
    ];

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // once, as soon as the track shows
        if (!_attaching && Value is not null)
        {
            _attaching = true;
            try
            {
                _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
                _self = DotNetObjectReference.Create(this);
                _attached = _module is null ? null : await _module.InvokeAsync<IJSObjectReference>("gradientTrack", _track, _self);
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    /// <summary>Adds a stop at <paramref name="position"/> with the color the gradient has there; nothing beyond <see cref="SignInFills.MaxStops"/>.</summary>
    [JSInvokable]
    public Task AddStop(int position)
    {
        if (Value is null || Value.Stops.Count >= SignInFills.MaxStops)
        {
            return Task.CompletedTask;
        }

        position = Math.Clamp(position, 0, 100);
        var stop = new SignInGradientStop { Color = ColorAt(position), Position = position };
        Value.Stops.Add(stop);
        _selected = stop;
        return SortAndNotifyAsync();
    }

    [JSInvokable]
    public void SelectStop(int index)
    {
        if (Value is not null && index >= 0 && index < Value.Stops.Count)
        {
            _selected = Value.Stops[index];
            StateHasChanged();
        }
    }

    /// <summary>Moves the selected stop to <paramref name="position"/>; it changes places with the stops it passes.</summary>
    [JSInvokable]
    public Task DragStop(int position) => SelectedStop is { } stop ? MoveAsync(stop, position) : Task.CompletedTask;

    internal Task MoveStopAsync(int index, int position)
    {
        SelectStop(index);
        return DragStop(position);
    }

    internal async Task RemoveStopAsync(int index)
    {
        if (Value is null || Value.Stops.Count <= 2 || index < 0 || index >= Value.Stops.Count)
        {
            return;
        }

        Value.Stops.RemoveAt(index);
        _selected = Value.Stops[Math.Min(index, Value.Stops.Count - 1)];
        await NotifyAsync();
    }

    // a new stop in the middle of the widest gap
    private Task AddInGapAsync()
    {
        var positions = Value!.Stops.Select(s => s.Position).Order().Prepend(0).Append(100).ToList();
        var (from, to) = positions.Zip(positions.Skip(1)).MaxBy(g => g.Second - g.First);
        return AddStop((from + to) / 2);
    }

    private Task MoveAsync(SignInGradientStop stop, int position)
    {
        position = Math.Clamp(position, 0, 100);
        if (stop.Position == position)
        {
            return Task.CompletedTask;
        }

        stop.Position = position;
        _selected = stop;
        return SortAndNotifyAsync();
    }

    private Task KeyAsync(KeyboardEventArgs e, int index)
    {
        var step = e.ShiftKey ? 10 : 1;
        return e.Key switch
        {
            "ArrowLeft" or "ArrowDown" => MoveStopAsync(index, Value!.Stops[index].Position - step),
            "ArrowRight" or "ArrowUp" => MoveStopAsync(index, Value!.Stops[index].Position + step),
            "Delete" or "Backspace" => RemoveStopAsync(index),
            _ => Task.CompletedTask,
        };
    }

    private Task SetColorAsync(string? color)
    {
        if (SelectedStop is not { } stop || !SignInFills.IsColor(color))
        {
            return Task.CompletedTask;
        }

        stop.Color = color!.Trim();
        return NotifyAsync();
    }

    private Task SetPositionAsync(int position) => SelectedStop is { } stop ? MoveAsync(stop, position) : Task.CompletedTask;

    private Task SetKindAsync(SignInGradientKind kind)
    {
        Value!.Kind = kind;
        return NotifyAsync();
    }

    private Task SetAngleAsync(int angle)
    {
        Value!.Angle = Math.Clamp(angle, 0, 359);
        return NotifyAsync();
    }

    private Task SetAnimatedAsync(bool animated)
    {
        Value!.Animated = animated;
        return NotifyAsync();
    }

    private Task UsePresetAsync(SignInGradient preset)
    {
        var copy = preset.Clone();
        (Value!.Kind, Value.Angle, Value.Animated, Value.Stops) = (copy.Kind, copy.Angle, copy.Animated, copy.Stops);
        _selected = Value.Stops[0];
        return NotifyAsync();
    }

    private Task SortAndNotifyAsync()
    {
        // stable: a stop dropped on another keeps its place in the list
        Value!.Stops = [.. Value.Stops.OrderBy(s => s.Position)];
        return NotifyAsync();
    }

    private async Task NotifyAsync()
    {
        await ValueChanged.InvokeAsync(Value!);
        StateHasChanged();
    }

    private string Show(string color) => SignInFills.ThemeColors.Contains(color) ? ThemeColor(color) : SignInFills.IsColor(color) ? color : "transparent";

    private static bool IsThemeColor(string color) => SignInFills.ThemeColors.Contains(color);

    // the color between the neighbouring stops; a theme color has no value here, so the nearer neighbour's color
    internal string ColorAt(int position)
    {
        var stops = Value!.Stops.OrderBy(s => s.Position).ToList();
        if (stops.Count == 0)
        {
            return "primary";
        }

        var before = stops.LastOrDefault(s => s.Position <= position) ?? stops[0];
        var after = stops.FirstOrDefault(s => s.Position >= position) ?? stops[^1];
        if (before == after || Parse(before.Color) is not { } from || Parse(after.Color) is not { } to)
        {
            return position - before.Position <= after.Position - position ? before.Color : after.Color;
        }

        var t = (double)(position - before.Position) / (after.Position - before.Position);
        byte Mix(byte a, byte b) => (byte)Math.Round(a + ((b - a) * t));
        var mixed = new MudColor(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B), Mix(from.A, to.A));
        return mixed.A == 255 ? mixed.ToString(MudColorOutputFormats.Hex) : mixed.ToString(MudColorOutputFormats.HexA);
    }

    private static MudColor? Parse(string color)
    {
        if (IsThemeColor(color) || color.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return new MudColor(color);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_attached is not null)
            {
                await _attached.InvokeVoidAsync("dispose");
                await _attached.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }

        _self?.Dispose();
        GC.SuppressFinalize(this);
    }
}
