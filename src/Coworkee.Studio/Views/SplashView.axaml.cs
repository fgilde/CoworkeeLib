using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Coworkee.Studio.Localization;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Views;

/// <summary>
/// The first content of the main window: the brand mark assembles while the Studio is built below it, then flies into the title bar.
/// The animations are composition animations, not XAML keyframes: they run on the render thread and keep moving while the UI thread
/// builds the view model and the main view, which takes seconds.
/// </summary>
public sealed partial class SplashView : UserControl
{
    private const int EntranceMs = 1100;
    private const int TransitionMs = 700;
    private static readonly Easing Out = new CubicEaseOut();
    private static readonly Easing Back = new BackEaseOut();
    private static readonly Easing InOut = new CubicEaseInOut();
    private static readonly Easing Sine = new SineEaseInOut();
    private readonly TaskCompletionSource _skip = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _shown = new();

    public SplashView()
    {
        InitializeComponent();
        VersionText.Text = L.Format("Version {0}", MainViewModel.StudioVersion);
    }

    /// <summary>False in tests: no animations and no waiting.</summary>
    public bool Animate { get; init; } = true;

    /// <summary>A plain fade instead of motion.</summary>
    public bool ReduceMotion { get; init; }

    public string? Status
    {
        get => StatusText.Text;
        set => StatusText.Text = value;
    }

    /// <summary>A start that failed: the reason and a way out instead of a splash that never ends.</summary>
    public void ShowError(string message)
    {
        Status = message;
        StatusText.Classes.Add("error");
        Track.IsVisible = false;
        CloseButton.IsVisible = true;
    }

    /// <summary>Uncovers <paramref name="studio"/> (laid out below the splash); the mark lands on <paramref name="titleLogo"/>. A click or Esc ends it at once.</summary>
    public async Task RevealAsync(Control studio, Visual? titleLogo)
    {
        if (!Animate)
        {
            return;
        }

        // the mark assembles once in full, unless the Studio was slower anyway
        if (!ReduceMotion && EntranceMs - _shown.ElapsedMilliseconds is > 0 and var rest)
        {
            await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(rest)), _skip.Task);
        }

        if (_skip.Task.IsCompleted)
        {
            return;
        }

        if (ReduceMotion)
        {
            Scalar(this, "Opacity", 250, 0, false, InOut, (0, 1), (1, 0));
            await Task.WhenAny(Task.Delay(250), _skip.Task);
            return;
        }

        Scalar(Texts, "Opacity", 220, 0, false, Out, (0, 1), (1, 0));
        Scalar(Progress, "Opacity", 160, 0, false, Out, (0, 1), (1, 0));
        Vector(Texts, "Translation", 220, 0, false, Out, (0, default), (1, new Vector3D(0, 10, 0)));
        Scalar(Glow, "Opacity", 320, 0, false, Out, (0, 1), (1, 0));
        Scalar(Backdrop, "Opacity", TransitionMs, 0, false, InOut, (0, 1), (0.25f, 1), (0.85f, 0), (1, 0));

        // the studio settles from slightly closer while the backdrop dissolves
        Center(studio);
        Vector(studio, "Scale", TransitionMs, 0, false, Out, (0, new Vector3D(1.03, 1.03, 1)), (0.25f, new Vector3D(1.03, 1.03, 1)), (1, new Vector3D(1, 1, 1)));

        // the mark swells a little, then flies to the logo of the title bar and shrinks to its size;
        // the logo of the title bar stays hidden until the mark has landed on it, so only one mark is seen
        if (TopLevel.GetTopLevel(this) is { } top && titleLogo is not null
            && LogoHost.TranslatePoint(default, top) is { } from && titleLogo.TranslatePoint(default, top) is { } to)
        {
            var center = Center(LogoHost);
            var scale = titleLogo.Bounds.Width / LogoHost.Bounds.Width;
            var target = new Vector3D(to.X - from.X - center.X * (1 - scale), to.Y - from.Y - center.Y * (1 - scale), 0);
            Vector(LogoHost, "Scale", TransitionMs, 0, false, InOut, (0, new Vector3D(1, 1, 1)), (0.2f, new Vector3D(1.08, 1.08, 1)), (1, new Vector3D(scale, scale, 1)));
            Vector(LogoHost, "Translation", TransitionMs, 0, false, InOut, (0, default), (0.2f, default), (1, target));
            titleLogo.Opacity = 0;
        }

        try
        {
            await Task.WhenAny(Task.Delay(TransitionMs), _skip.Task);
            ElementComposition.GetElementVisual(studio)?.StopAnimation("Scale");
        }
        finally
        {
            // in the same frame in which the caller removes the splash
            if (titleLogo is not null)
            {
                titleLogo.Opacity = 1;
            }
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _shown.Start();
        TopLevel.GetTopLevel(this)?.AddHandler(KeyDownEvent, SkipOnEscape, RoutingStrategies.Tunnel);
        if (!Animate)
        {
            return;
        }

        if (ReduceMotion)
        {
            Track.IsVisible = false;
            Scalar(this, "Opacity", 250, 0, false, Out, (0, 0), (1, 1));
            return;
        }

        Scalar(Glow, "Opacity", 900, 100, false, Out, (0, 0), (1, 1));
        Grow(Glow, 900, 100, 0.5, Out);
        Vector(GlowPulse, "Scale", 2600, 0, true, InOut, (0, new Vector3D(1, 1, 1)), (0.5f, new Vector3D(1.1, 1.1, 1)), (1, new Vector3D(1, 1, 1)));
        Scalar(GlowPulse, "Opacity", 2600, 0, true, InOut, (0, 1), (0.5f, 0.65f), (1, 1));

        // the outer C sweeps in, the inner part follows, the two wings swing in last
        Arrive(Outer, 0, 850, default, -1.6f, 0.4);
        Arrive(Inner, 180, 750, new Vector3D(-26, 10, 0), 0, 0.7);
        Arrive(TopWing, 420, 650, new Vector3D(34, -18, 0), 0.5f, 0.8);
        Arrive(BottomWing, 500, 650, new Vector3D(34, 18, 0), -0.5f, 0.8);
        Vector(LogoFloat, "Translation", 3200, EntranceMs, true, InOut, (0, default), (0.5f, new Vector3D(0, -4, 0)), (1, default));

        Scalar(Texts, "Opacity", 700, 520, false, Out, (0, 0), (1, 1));
        Vector(Texts, "Translation", 700, 520, false, Out, (0, new Vector3D(0, 14, 0)), (1, default));
        Scalar(Progress, "Opacity", 500, 800, false, Out, (0, 0), (1, 1));
        Vector(Runner, "Translation", 1400, 0, true, Sine, (0, new Vector3D(-72, 0, 0)), (1, new Vector3D(180, 0, 0)));
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        TopLevel.GetTopLevel(this)?.RemoveHandler(KeyDownEvent, SkipOnEscape);
        base.OnUnloaded(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _skip.TrySetResult();
    }

    private void SkipOnEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _skip.TrySetResult();
        }
    }

    private void CloseWindow(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();

    /// <summary>A part of the mark fades in from an offset, a turn and a smaller size, all around the centre of the mark.</summary>
    private static void Arrive(Visual part, int delay, int duration, Vector3D offset, float turn, double size)
    {
        Center(part);
        Scalar(part, "Opacity", duration / 2, delay, false, Out, (0, 0), (1, 1));
        Vector(part, "Translation", duration, delay, false, Out, (0, offset), (1, default));
        Scalar(part, "RotationAngle", duration, delay, false, Out, (0, turn), (1, 0));
        Grow(part, duration, delay, size, Back);
    }

    private static void Grow(Visual control, int duration, int delay, double from, Easing easing)
    {
        Center(control);
        Vector(control, "Scale", duration, delay, false, easing, (0, new Vector3D(from, from, 1)), (1, new Vector3D(1, 1, 1)));
    }

    /// <summary>Scale and rotation work around the centre of the control instead of its top left corner.</summary>
    private static Vector3D Center(Visual control)
    {
        var center = new Vector3D(control.Bounds.Width / 2, control.Bounds.Height / 2, 0);
        if (ElementComposition.GetElementVisual(control) is { } visual)
        {
            visual.CenterPoint = center;
        }

        return center;
    }

    private static void Scalar(Visual control, string property, int duration, int delay, bool loop, Easing easing, params (float Key, float Value)[] frames)
    {
        if (ElementComposition.GetElementVisual(control) is not { } visual)
        {
            return;
        }

        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        foreach (var (key, value) in frames)
        {
            animation.InsertKeyFrame(key, value, easing);
        }

        visual.StartAnimation(property, Timed(animation, duration, delay, loop));
    }

    private static void Vector(Visual control, string property, int duration, int delay, bool loop, Easing easing, params (float Key, Vector3D Value)[] frames)
    {
        if (ElementComposition.GetElementVisual(control) is not { } visual)
        {
            return;
        }

        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        foreach (var (key, value) in frames)
        {
            animation.InsertKeyFrame(key, value, easing);
        }

        visual.StartAnimation(property, Timed(animation, duration, delay, loop));
    }

    private static KeyFrameAnimation Timed(KeyFrameAnimation animation, int duration, int delay, bool loop)
    {
        animation.Duration = TimeSpan.FromMilliseconds(duration);
        animation.DelayTime = TimeSpan.FromMilliseconds(delay);
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        animation.IterationBehavior = loop ? AnimationIterationBehavior.Forever : AnimationIterationBehavior.Count;
        return animation;
    }
}
