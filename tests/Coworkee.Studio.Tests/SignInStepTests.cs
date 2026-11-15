using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Coworkee.Cli.Spec;
using Coworkee.Studio.ViewModels;
using Coworkee.Studio.Views;

namespace Coworkee.Studio.Tests;

public sealed class SignInStepTests
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("STUDIO_SCREENSHOTS");

    [Fact]
    public Task The_step_offers_the_seven_layouts_and_only_the_options_of_the_chosen_one() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        var step = main.SignIn;

        step.Layouts.Select(l => l.Value).ShouldBe(["Card", "Split", "Slide", "Spotlight", "Poster", "Stack", "Minimal"]);

        main.Option("signInLayout").Value = "Card";
        (step.HasPanel, step.Offers.Surface, step.Offers.Animation, step.Offers.Dim, step.Offers.MediaSide, step.Offers.Tilt).ShouldBe((false, true, true, true, false, false));
        step.Page.Title.ShouldBe("Page background");

        main.Option("signInLayout").Value = "Split";
        (step.HasPanel, step.Offers.Surface, step.Offers.MediaSide, step.Offers.Dim).ShouldBe((true, false, true, false));
        step.Page.Title.ShouldBe("Form side");
        step.Panel.Title.ShouldBe("Media side");
        step.Panel.Modes.ShouldBe([FillSlotViewModel.Default, FillSlotViewModel.File, FillSlotViewModel.Color, FillSlotViewModel.Gradient]);

        main.Option("signInLayout").Value = "Poster";
        step.Panel.Title.ShouldBe("Poster");

        main.Option("signInLayout").Value = "Stack";
        (step.Offers.Tilt, step.Offers.MediaSide, step.Panel.Title).ShouldBe((true, false, "Cards behind"));

        main.Option("signInLayout").Value = "Minimal";
        (step.Offers.LogoSize, step.HasPanel).ShouldBe((true, false));

        // without a second background only the loading screen needs media, and no fill
        step.LoadingMedia = true;
        step.ShowsPanel.ShouldBeTrue();
        step.Panel.Modes.ShouldBe([FillSlotViewModel.Default, FillSlotViewModel.File]);
    });

    [Fact]
    public Task Fills_and_looks_go_into_the_spec_and_options_of_other_layouts_stay_out() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        var step = main.SignIn;
        main.Option("signInLayout").Value = "Spotlight";
        step.Page.Mode = FillSlotViewModel.Gradient;
        step.Surface = "Glass";
        step.Aurora = true;
        step.Dim = 30;
        step.MediaRight = true;

        var branding = main.ToSpec().Branding!;
        branding.SignInLayout.ShouldBe("Spotlight");
        branding.SignInPageFill!.Gradient!.Stops.Count.ShouldBe(2);
        branding.SignInPageFill.Gradient.Angle.ShouldBe(135);
        (branding.SignInSurface, branding.SignInAnimation, branding.SignInBackgroundDim, branding.SignInMediaSide).ShouldBe(("Glass", "Aurora", 30, "Right"));

        step.Page.Mode = FillSlotViewModel.Color;
        step.Page.ColorText = "primary";
        main.ToSpec().Branding!.SignInPageFill.ShouldBeEquivalentTo(new SignInFillSpec { Color = "primary" });

        // Stack has no surface and no side, so they stay out of the spec
        main.Option("signInLayout").Value = "Stack";
        step.Tilt = 9;
        branding = main.ToSpec().Branding!;
        (branding.SignInSurface, branding.SignInMediaSide, branding.SignInTilt, branding.SignInAnimation).ShouldBe((null, null, 9, "Aurora"));
        main.Errors.ShouldBeEmpty();
    });

    [Fact]
    public Task A_slot_holds_one_background_at_a_time() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Option("signInLayout").Value = "Slide";
        var panel = main.SignIn.Panel;
        panel.Mode = FillSlotViewModel.File;
        panel.FilePath = "https://cdn.test/a.mp4";

        panel.Mode = FillSlotViewModel.Color;
        main.Option("signInMedia").Value.ShouldBeNull();
        SignInStyle.Parse(main.Option("signInPanelFill").Value)!.Color.ShouldNotBeNull();

        panel.Mode = FillSlotViewModel.Default;
        main.Option("signInPanelFill").Value.ShouldBeNull();
    });

    [Fact]
    public Task Gradient_stops_are_added_in_the_color_there_and_removed_down_to_two() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        var page = main.SignIn.Page;
        page.Mode = FillSlotViewModel.Gradient;
        var gradient = page.GradientEditor!;
        gradient.Stops[0].Text = "#000000";
        gradient.Stops[1].Text = "#ffffff";

        var middle = gradient.AddStop(50)!;
        middle.Text.ShouldBe("#808080");
        gradient.SelectedStop.ShouldBe(middle);
        SignInStyle.Parse(main.Option("signInPageFill").Value)!.Gradient!.Stops.Select(s => s.Position).ShouldBe([0, 50, 100]);

        for (var i = 0; i < 10; i++)
        {
            gradient.AddStop(10 + i);
        }

        gradient.Stops.Count.ShouldBe(SignInStyle.MaxStops);
        gradient.CanAdd.ShouldBeFalse();

        while (gradient.RemoveStopCommand.CanExecute(null))
        {
            gradient.RemoveStopCommand.Execute(null);
        }

        gradient.Stops.Count.ShouldBe(2);
        gradient.KindName = "Conic";
        gradient.Angle = 40;
        gradient.Animated = true;
        var stored = SignInStyle.Parse(main.Option("signInPageFill").Value)!.Gradient!;
        (stored.Kind, stored.Angle, stored.Animated, stored.Stops.Count).ShouldBe((GradientKind.Conic, 40, true, 2));
        gradient.KindName = "Radial";
        gradient.HasAngle.ShouldBeFalse();
    });

    [Fact]
    public Task A_spec_with_the_old_layouts_Glass_or_Aurora_opens_as_Card_with_that_look() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);

        main.Load(SpecJson.Read("""{ "name": "Shop", "branding": { "signInLayout": "Glass" } }"""), null);
        (main.SignIn.Layout, main.SignIn.Surface, main.SignIn.Aurora).ShouldBe(("Card", "Glass", false));

        main.Load(SpecJson.Read("""{ "name": "Shop", "branding": { "signInLayout": "Aurora", "signInPageFill": { "gradient": { "kind": "radial", "stops": [ { "color": "#112233", "position": 0 }, { "color": "surface", "position": 100 } ] } } } }"""), null);
        (main.SignIn.Layout, main.SignIn.Surface, main.SignIn.Aurora).ShouldBe(("Card", "Solid", true));
        main.SignIn.Page.Mode.ShouldBe(FillSlotViewModel.Gradient);
        main.SignIn.Page.GradientEditor!.Kind.ShouldBe(GradientKind.Radial);
        main.ToSpec().Branding!.SignInLayout.ShouldBe("Card");
    });

    [Fact]
    public void Fill_colors_read_like_css_and_gradients_point_like_css()
    {
        var palette = Model.ThemeInfo.Find("Forest").Light;
        Model.FillBrush.Resolve("#11223380", palette).ShouldBe(Avalonia.Media.Color.FromArgb(0x80, 0x11, 0x22, 0x33));
        Model.FillBrush.Resolve("primary", palette).ShouldBe(palette.Primary);
        Model.FillBrush.Resolve("rgb(1, 2, 3)", palette).ShouldBe(Avalonia.Media.Color.FromRgb(1, 2, 3));
        Model.FillBrush.Hex(Avalonia.Media.Color.FromArgb(0x80, 0x11, 0x22, 0x33)).ShouldBe("#11223380");
        Model.FillBrush.Hex(Avalonia.Media.Color.FromRgb(0x11, 0x22, 0x33)).ShouldBe("#112233");

        var right = (Avalonia.Media.LinearGradientBrush)Model.FillBrush.Gradient(new SignInGradientSpec { Angle = 90, Stops = [new() { Color = "#000", Position = 0 }, new() { Color = "#fff", Position = 100 }] }, c => Avalonia.Media.Color.Parse(c));
        right.StartPoint.Point.X.ShouldBe(0, 1e-9);
        right.EndPoint.Point.X.ShouldBe(1, 1e-9);
        Model.FillBrush.Gradient(new SignInGradientSpec { Kind = GradientKind.Conic, Stops = [] }, c => Avalonia.Media.Colors.Black).ShouldBeOfType<Avalonia.Media.ConicGradientBrush>();
    }

    public static TheoryData<string> Layouts => [.. SignInLayoutOptions.Layouts];

    [Theory]
    [MemberData(nameof(Layouts))]
    public Task Every_layout_renders_with_its_options(string layout) => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        main.Option("signInLayout").Value = layout;
        var step = main.SignIn;
        if (step.Offers.Animation && layout is "Spotlight" or "Minimal")
        {
            step.Aurora = true;
        }

        if (step.Offers.Surface && layout == "Spotlight")
        {
            step.Surface = "Glass";
            step.Page.Mode = FillSlotViewModel.Gradient;
            step.Page.GradientEditor!.AddStop(50)!.Text = "#1d3c78";
        }

        if (layout == "Poster")
        {
            step.Panel.Mode = FillSlotViewModel.Gradient;
            step.Panel.GradientEditor!.KindName = "Radial";
        }

        var frame = Render.Step(main, step, dark: false, window => Scroll(window, "SignInStyle"));
        frame.PixelSize.Width.ShouldBe(1400);
        Save(frame, $"studio-signin-{layout.ToLowerInvariant()}.png");

        var thumb = new SignInThumb { Layout = layout, Preview = step.Preview, Sketch = step.Sketch, Width = 640, Height = 400 };
        var window = new Window { Content = thumb, Width = 640, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(5);
        var sketch = window.CaptureRenderedFrame().ShouldNotBeNull();
        window.Close();
        Save(sketch, $"studio-signin-thumb-{layout.ToLowerInvariant()}.png");
    });

    [Fact]
    public Task The_gradient_control_renders_its_stops() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        main.Option("signInLayout").Value = "Split";
        var step = main.SignIn;
        step.Page.Mode = FillSlotViewModel.Gradient;
        var gradient = step.Page.GradientEditor!;
        gradient.AddStop(35)!.Text = "#7c4dff";
        gradient.AddStop(70)!.Text = "#00bcd4";
        gradient.Select(gradient.Stops[2]);
        step.Panel.Mode = FillSlotViewModel.Color;

        var frame = Render.Step(main, step, dark: false, window => Scroll(window, "SignInStyle"));
        Save(frame, "studio-signin-gradient.png");
        Save(Render.Step(main, step, dark: true, window => Scroll(window, "SignInStyle")), "studio-signin-gradient-dark.png");
    });

    private static void Scroll(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var target = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Control>().First(c => c.Name == name);
        target.BringIntoView();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Avalonia.Media.Imaging.Bitmap frame, string name)
    {
        if (Folder is not null)
        {
            Directory.CreateDirectory(Folder);
            frame.Save(Path.Combine(Folder, name));
        }
    }
}
