using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;

namespace Coworkee.Studio.ViewModels;

/// <summary>What the sign-in sketches draw besides the layout: backgrounds, side, surface, animation, darkening, tilt and logo size.</summary>
public sealed record SignInSketch(
    IImage? Media, IImage? PageImage, IBrush? PageFill, IBrush? PanelFill, bool MediaRight, bool Glass, bool Aurora, double Dim, double Tilt, string LogoSize);

public sealed partial class SignInStep : StepViewModel
{
    /// <summary>The runtime's tilt while none is set.</summary>
    public const int DefaultTilt = 6;

    public SignInStep(MainViewModel main)
        : base(main)
    {
        var choices = OptionCatalog.Find("signInLayout")!.Choices!;
        Layouts = [.. SignInLayoutOptions.Layouts.Select(l => new LayoutCardViewModel(l, choices.Single(c => c.Value == l).Description))];
        Page = new FillSlotViewModel(main, "signInBackground", "signInPageFill", () => Preview.Palette);
        Panel = new FillSlotViewModel(main, "signInMedia", "signInPanelFill", () => Preview.Palette);
    }

    protected override string TitleText => "Sign-in";

    protected override string SubtitleText => "How the sign-in, registration and password pages look.";

    protected override string PitchText => "The auth server brings sign-in, step-by-step registration, password reset and external logins. Pick a design now; administrators can switch it later on the Themes page.";

    public override string Icon => "Icon.Login";

    public static IReadOnlyList<string> Surfaces { get; } = ["Solid", "Glass"];

    public static IReadOnlyList<string> LogoSizes { get; } = ["Small", "Medium", "Large"];

    public IReadOnlyList<LayoutCardViewModel> Layouts { get; }

    /// <summary>Background 1: the page, or the form side of Split and Poster.</summary>
    public FillSlotViewModel Page { get; }

    /// <summary>Background 2: the media or panel, the poster, the cards behind.</summary>
    public FillSlotViewModel Panel { get; }

    public string Layout => Main.Option("signInLayout").Value ?? "Slide";

    /// <summary>What the layout offers (the same table as the Themes page of the app).</summary>
    public SignInLayoutOptions Offers => SignInLayoutOptions.For(Layout);

    public bool HasPanel => Offers.PanelLabel is not null;

    /// <summary>The second background shows also without a panel when the loading screen needs its media.</summary>
    public bool ShowsPanel => HasPanel || LoadingMedia;

    public bool MediaLeft
    {
        get => Main.Option("signInMediaSide").Value != "Right";
        set => Main.Option("signInMediaSide").Value = value ? "Left" : "Right";
    }

    public bool MediaRight
    {
        get => !MediaLeft;
        set => MediaLeft = !value;
    }

    public string Surface
    {
        get => Main.Option("signInSurface").Value ?? "Solid";
        set => Main.Option("signInSurface").Value = value;
    }

    public bool Aurora
    {
        get => Main.Option("signInAnimation").Value == "Aurora";
        set => Main.Option("signInAnimation").Value = value ? "Aurora" : "None";
    }

    public string LogoSize
    {
        get => Main.Option("signInLogoSize").Value ?? "Medium";
        set => Main.Option("signInLogoSize").Value = value;
    }

    public int Dim
    {
        get => IntOption("signInBackgroundDim") ?? 0;
        set => Main.Option("signInBackgroundDim").Value = value == 0 ? null : value.ToString(CultureInfo.InvariantCulture);
    }

    public int Tilt
    {
        get => IntOption("signInTilt") ?? DefaultTilt;
        set => Main.Option("signInTilt").Value = value.ToString(CultureInfo.InvariantCulture);
    }

    public bool LoadingAnimated
    {
        get => LoadingScreen == "Animated";
        set => SetLoadingScreen(value, "Animated");
    }

    public bool LoadingMedia
    {
        get => LoadingScreen == "SignInMedia";
        set => SetLoadingScreen(value, "SignInMedia");
    }

    public bool LoadingPlain
    {
        get => LoadingScreen == "Plain";
        set => SetLoadingScreen(value, "Plain");
    }

    private string LoadingScreen => Main.Option("loadingScreen").Value ?? "Animated";

    public SignInSketch Sketch => new(Panel.Image, Page.Image, Page.Brush, Panel.Brush, MediaRight, Surface == "Glass", Aurora,
        Page.Image is null ? 0 : Dim / 100d, Tilt, LogoSize);

    public PreviewViewModel Preview => Main.Branding.PreviewLight;

    public override bool Owns(string error) => error.StartsWith("signIn", StringComparison.Ordinal);

    [RelayCommand]
    public void SelectLayout(LayoutCardViewModel card) => Main.Option("signInLayout").Value = card.Value;

    public void Sync()
    {
        Page.Sync();
        Panel.Sync();
        Refresh();
    }

    private int? IntOption(string id) => int.TryParse(Main.Option(id).Value, CultureInfo.InvariantCulture, out var number) ? number : null;

    private void SetLoadingScreen(bool chosen, string value)
    {
        if (chosen)
        {
            Main.Option("loadingScreen").Value = value;
        }
    }

    public override void Refresh()
    {
        foreach (var card in Layouts)
        {
            card.IsSelected = card.Value == Layout;
        }

        Page.Label = Offers.PageLabel;
        Panel.Label = Offers.PanelLabel ?? "Loading screen";
        Panel.AllowsFill = HasPanel;
        Page.Refresh();
        Panel.Refresh();
        OnPropertyChanged(string.Empty);
    }
}
