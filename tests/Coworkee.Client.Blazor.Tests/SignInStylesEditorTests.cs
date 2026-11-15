using Bunit;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The sign-in design card shows only the options of the chosen layout; its backgrounds are fills with a multi-stop gradient control.</summary>
public sealed class SignInStylesEditorTests : ClientTestBase
{
    public SignInStylesEditorTests()
    {
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([]);
        Render<MudPopoverProvider>();
    }

    [Theory]
    [InlineData(SignInLayouts.Card, 1, false, true, true, true, false, false)]
    [InlineData(SignInLayouts.Split, 2, true, false, false, false, false, false)]
    [InlineData(SignInLayouts.Slide, 2, true, false, false, true, false, false)]
    [InlineData(SignInLayouts.Spotlight, 1, true, true, true, true, false, false)]
    [InlineData(SignInLayouts.Poster, 2, true, false, false, false, false, false)]
    [InlineData(SignInLayouts.Stack, 2, false, false, true, true, true, false)]
    [InlineData(SignInLayouts.Minimal, 1, false, false, true, true, false, true)]
    public void The_card_shows_only_the_options_of_the_layout(string layout, int backgrounds, bool side, bool surface, bool aurora, bool dim, bool tilt, bool logo)
    {
        var card = RenderCard(new NamedSignInDesign { Layout = layout });

        var fills = card.FindComponents<FillEditor>();
        fills.Count.ShouldBe(backgrounds);
        var options = SignInLayoutOptions.For(layout);
        fills[0].Instance.Label.ShouldBe(options.PageBackground);
        if (backgrounds == 2)
        {
            fills[1].Instance.Label.ShouldBe(options.PanelBackground);
        }

        card.FindAll("[data-testid='sign-in-side']").Count.ShouldBe(side ? 1 : 0);
        card.FindAll("[data-testid='sign-in-surface']").Count.ShouldBe(surface ? 1 : 0);
        card.FindAll("[data-testid='sign-in-aurora']").Count.ShouldBe(aurora ? 1 : 0);
        card.FindAll("[data-testid='sign-in-background-dim']").Count.ShouldBe(dim ? 1 : 0);
        card.FindAll("[data-testid='sign-in-tilt']").Count.ShouldBe(tilt ? 1 : 0);
        card.FindAll("[data-testid='sign-in-logo-size']").Count.ShouldBe(logo ? 1 : 0);
        if (side)
        {
            card.FindComponent<MudSelect<SignInMediaSide?>>().Instance.Label.ShouldBe(options.MediaSide);
        }
    }

    [Fact]
    public async Task Choosing_another_layout_changes_the_options_shown()
    {
        var design = new NamedSignInDesign { Layout = SignInLayouts.Card };
        var card = RenderCard(design);
        card.FindAll("[data-testid='sign-in-tilt']").ShouldBeEmpty();

        await card.InvokeAsync(() => card.Find("[data-layout='Stack']").ClickAsync(new()));

        design.Layout.ShouldBe(SignInLayouts.Stack);
        card.FindAll("[data-testid='sign-in-tilt']").ShouldHaveSingleItem();
        card.FindAll("[data-testid='sign-in-surface']").ShouldBeEmpty();
        card.FindComponents<FillEditor>().Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_options_write_the_design_and_solid_or_no_aurora_stay_empty()
    {
        var changes = 0;
        var design = new NamedSignInDesign { Layout = SignInLayouts.Card };
        var card = RenderCard(design, () => changes++);

        await card.InvokeAsync(() => card.FindComponent<MudToggleGroup<SignInSurface>>().Instance.ValueChanged.InvokeAsync(SignInSurface.Glass));
        await card.InvokeAsync(() => card.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));
        await card.InvokeAsync(() => card.FindComponent<MudSlider<int>>().Instance.ValueChanged.InvokeAsync(35));

        design.ShouldSatisfyAllConditions(d => d.Surface.ShouldBe(SignInSurface.Glass), d => d.Animation.ShouldBe(SignInAnimation.Aurora), d => d.BackgroundDim.ShouldBe(35));
        await card.InvokeAsync(() => card.FindComponent<MudToggleGroup<SignInSurface>>().Instance.ValueChanged.InvokeAsync(SignInSurface.Solid));
        await card.InvokeAsync(() => card.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(false));
        design.Surface.ShouldBeNull();
        design.Animation.ShouldBeNull();
        changes.ShouldBe(5);
    }

    [Fact]
    public async Task Tilt_and_logo_size_reach_the_design()
    {
        var stack = new NamedSignInDesign { Layout = SignInLayouts.Stack };
        var minimal = new NamedSignInDesign { Layout = SignInLayouts.Minimal };
        var stackCard = RenderCard(stack);
        var minimalCard = RenderCard(minimal);

        await stackCard.InvokeAsync(() => stackCard.FindComponents<MudSlider<int>>().Single(s => s.Instance.Max == SignInLayoutOptions.MaxTilt).Instance.ValueChanged.InvokeAsync(11));
        await minimalCard.InvokeAsync(() => minimalCard.FindComponent<MudToggleGroup<SignInLogoSize>>().Instance.ValueChanged.InvokeAsync(SignInLogoSize.Large));

        stack.Tilt.ShouldBe(11);
        minimal.LogoSize.ShouldBe(SignInLogoSize.Large);
    }

    [Theory]
    [InlineData("Glass", SignInSurface.Glass, null)]
    [InlineData("Aurora", null, SignInAnimation.Aurora)]
    public void A_stored_glass_or_aurora_design_opens_as_the_card_it_became(string layout, SignInSurface? surface, SignInAnimation? animation)
    {
        var design = new NamedSignInDesign { Layout = layout };

        var card = RenderCard(design);

        design.Layout.ShouldBe(SignInLayouts.Card);
        design.Surface.ShouldBe(surface);
        design.Animation.ShouldBe(animation);
        card.Find("[data-layout='Card']").GetAttribute("aria-checked").ShouldBe("true");
    }

    [Fact]
    public async Task The_fill_editor_switches_between_theme_file_color_and_gradient_and_keeps_one_value()
    {
        string? url = "/media/sky.jpg";
        SignInFill? fill = null;
        var editor = RenderFill(() => url, u => url = u, () => fill, f => fill = f);
        editor.Instance.Mode.ShouldBe(FillMode.File);

        await editor.InvokeAsync(() => editor.Instance.SetModeAsync(FillMode.Color));
        url.ShouldBeNull();
        fill!.Color.ShouldBe("primary");
        editor.Instance.Mode.ShouldBe(FillMode.Color);
        editor.Find("[data-testid='fill-preview']").GetAttribute("style")!.ShouldContain("var(--mud-palette-primary)");

        await editor.InvokeAsync(() => editor.FindAll("[data-testid='theme-color']").Single(c => c.GetAttribute("data-color") == "secondary").ClickAsync(new()));
        fill!.Color.ShouldBe("secondary");

        await editor.InvokeAsync(() => editor.Instance.SetModeAsync(FillMode.Gradient));
        fill!.Gradient!.Stops.Select(s => s.Color).ShouldBe(["secondary", "tertiary"]);
        editor.FindAll("[data-testid='gradient-editor']").ShouldHaveSingleItem();

        await editor.InvokeAsync(() => editor.Instance.SetModeAsync(FillMode.Color));
        fill!.Color.ShouldBe("secondary");
        fill.Gradient.ShouldBeNull();

        await editor.InvokeAsync(() => editor.Instance.SetModeAsync(FillMode.Theme));
        fill.ShouldBeNull();
        url.ShouldBeNull();
        editor.Instance.Mode.ShouldBe(FillMode.Theme);
    }

    [Fact]
    public async Task An_address_replaces_the_fill_and_the_file_mode_stays_until_one_is_typed()
    {
        string? url = null;
        SignInFill? fill = new() { Color = "#123456" };
        var editor = RenderFill(() => url, u => url = u, () => fill, f => fill = f);

        await editor.InvokeAsync(() => editor.Instance.SetModeAsync(FillMode.File));
        editor.Instance.Mode.ShouldBe(FillMode.File);
        fill.ShouldBeNull();
        editor.Find("[data-testid='fill-url'] input, input[data-testid='fill-url']").Change("/media/clip.mp4");

        url.ShouldBe("/media/clip.mp4");
        editor.Instance.Mode.ShouldBe(FillMode.File);
    }

    [Fact]
    public void Without_files_the_editor_offers_no_image()
    {
        var editor = Render<FillEditor>(p => p.Add(f => f.AllowFiles, false).Add(f => f.Url, "/media/sky.jpg"));

        editor.Instance.Mode.ShouldBe(FillMode.Theme);
        editor.FindComponents<MudToggleItem<FillMode>>().Select(i => i.Instance.Value).ShouldBe([FillMode.Theme, FillMode.Color, FillMode.Gradient]);
    }

    [Fact]
    public async Task A_click_on_the_track_adds_a_stop_with_the_color_at_that_point()
    {
        var gradient = new SignInGradient { Stops = [new() { Color = "#000000", Position = 0 }, new() { Color = "#ffffff", Position = 100 }] };
        var editor = RenderGradient(gradient);

        await editor.InvokeAsync(() => editor.Instance.AddStop(50));

        gradient.Stops.Select(s => (s.Color, s.Position)).ShouldBe([("#000000", 0), ("#808080", 50), ("#ffffff", 100)]);
        editor.Instance.Selected.ShouldBe(1);
        editor.FindAll("[data-testid='gradient-stop']").Count.ShouldBe(3);
        editor.FindAll("[data-testid='gradient-stop']")[1].GetAttribute("aria-pressed").ShouldBe("true");
    }

    [Fact]
    public async Task Next_to_theme_colors_a_new_stop_takes_the_nearer_color_and_there_are_at_most_eight()
    {
        var gradient = GradientEditor.Default();
        var editor = RenderGradient(gradient);

        await editor.InvokeAsync(() => editor.Instance.AddStop(20));
        gradient.Stops[1].Color.ShouldBe("primary");
        for (var i = 0; i < 10; i++)
        {
            await editor.InvokeAsync(() => editor.Instance.AddStop(30 + i));
        }

        gradient.Stops.Count.ShouldBe(SignInFills.MaxStops);
        editor.Find("[data-testid='gradient-stop-add']").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task Dragging_a_stop_past_another_reorders_them_and_keeps_it_selected()
    {
        var gradient = new SignInGradient { Stops = [new() { Color = "#ff0000", Position = 0 }, new() { Color = "#00ff00", Position = 50 }, new() { Color = "#0000ff", Position = 100 }] };
        var editor = RenderGradient(gradient);

        await editor.InvokeAsync(() => editor.Instance.SelectStop(0));
        await editor.InvokeAsync(() => editor.Instance.DragStop(75));
        await editor.InvokeAsync(() => editor.Instance.DragStop(140));

        gradient.Stops.Select(s => (s.Color, s.Position)).ShouldBe([("#00ff00", 50), ("#ff0000", 100), ("#0000ff", 100)]);
        editor.Instance.Selected.ShouldBe(1);
        editor.Instance.Css.ShouldBe("linear-gradient(135deg, #00ff00 50%, #ff0000 100%, #0000ff 100%)");
    }

    [Fact]
    public async Task The_arrow_keys_move_the_stop_and_delete_removes_it_but_two_stay()
    {
        var gradient = new SignInGradient { Stops = [new() { Color = "#ff0000", Position = 10 }, new() { Color = "#0000ff", Position = 90 }] };
        var editor = RenderGradient(gradient);

        await editor.InvokeAsync(() => editor.FindAll("[data-testid='gradient-stop']")[0].KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" }));
        await editor.InvokeAsync(() => editor.FindAll("[data-testid='gradient-stop']")[0].KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true }));
        gradient.Stops[0].Position.ShouldBe(21);

        await editor.InvokeAsync(() => editor.FindAll("[data-testid='gradient-stop']")[0].KeyDownAsync(new KeyboardEventArgs { Key = "Delete" }));
        gradient.Stops.Count.ShouldBe(2);
        editor.Find("[data-testid='gradient-stop-delete']").HasAttribute("disabled").ShouldBeTrue();

        await editor.InvokeAsync(() => editor.Find("[data-testid='gradient-stop-add']").ClickAsync(new()));
        gradient.Stops.Select(s => s.Position).ShouldBe([21, 55, 90]);
        await editor.InvokeAsync(() => editor.Instance.RemoveStopAsync(1));
        gradient.Stops.Select(s => s.Position).ShouldBe([21, 90]);
    }

    [Fact]
    public async Task Kind_angle_motion_theme_colors_and_presets_change_the_gradient_and_its_preview()
    {
        var changes = 0;
        var gradient = GradientEditor.Default();
        var editor = RenderGradient(gradient, () => changes++);

        await editor.InvokeAsync(() => editor.FindComponent<MudToggleGroup<SignInGradientKind>>().Instance.ValueChanged.InvokeAsync(SignInGradientKind.Conic));
        await editor.InvokeAsync(() => editor.FindComponent<MudSlider<int>>().Instance.ValueChanged.InvokeAsync(400));
        await editor.InvokeAsync(() => editor.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));
        await editor.InvokeAsync(() => editor.FindAll("[data-testid='theme-color']").Single(c => c.GetAttribute("data-color") == "surface").ClickAsync(new()));

        gradient.ShouldSatisfyAllConditions(g => g.Kind.ShouldBe(SignInGradientKind.Conic), g => g.Angle.ShouldBe(359), g => g.Animated.ShouldBeTrue());
        gradient.Stops[0].Color.ShouldBe("surface");
        editor.Find("[data-testid='gradient-preview']").GetAttribute("style")!
            .ShouldBe("background:conic-gradient(from 359deg at 50% 50%, var(--mud-palette-surface) 0%, var(--mud-palette-tertiary) 100%)");
        editor.Find("[data-testid='gradient-preview']").ClassList.ShouldContain("animated");

        await editor.InvokeAsync(() => editor.FindAll("[data-testid='gradient-preset']")[2].ClickAsync(new()));
        gradient.Stops.Select(s => s.Color).ShouldBe(["#ff5f6d", "#ffc371"]);
        gradient.Kind.ShouldBe(SignInGradientKind.Linear);
        changes.ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task The_track_follows_the_pointer_through_the_script()
    {
        var gradient = GradientEditor.Default();
        var editor = RenderGradient(gradient);

        await editor.InvokeAsync(() => editor.Instance.MoveStopAsync(1, 60));

        gradient.Stops[1].Position.ShouldBe(60);
        editor.Find("[data-testid='gradient-track']").GetAttribute("style")!.ShouldContain("var(--mud-palette-tertiary) 60%");
        JSInterop.VerifyInvoke("import");
    }

    private IRenderedComponent<SignInDesignCard> RenderCard(SignInLook design, Action? changed = null)
    {
        return Render<SignInDesignCard>(p => p.Add(c => c.Value, design).Add(c => c.Changed, () => changed?.Invoke()));
    }

    private IRenderedComponent<FillEditor> RenderFill(Func<string?> url, Action<string?> setUrl, Func<SignInFill?> fill, Action<SignInFill?> setFill)
    {
        IRenderedComponent<FillEditor>? editor = null;
        editor = Render<FillEditor>(p => p.Add(f => f.Url, url()).Add(f => f.Fill, fill())
            .Add(f => f.UrlChanged, u =>
            {
                setUrl(u);
                editor?.Render(q => q.Add(f => f.Url, url()));
            })
            .Add(f => f.FillChanged, v =>
            {
                setFill(v);
                editor?.Render(q => q.Add(f => f.Fill, fill()));
            }));
        return editor;
    }

    private IRenderedComponent<GradientEditor> RenderGradient(SignInGradient gradient, Action? changed = null)
    {
        return Render<GradientEditor>(p => p.Add(g => g.Value, gradient).Add(g => g.ValueChanged, _ => changed?.Invoke()));
    }
}
