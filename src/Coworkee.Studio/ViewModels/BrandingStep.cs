using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

public sealed partial class BrandingStep : StepViewModel
{
    private static readonly string[] BrandIds = ["theme", "primaryColor", "secondaryColor", "logo"];
    // per instance: a bitmap belongs to the thread that loaded it
    private readonly Bitmap? _defaultLogo = Images.Asset("coworkee.png");
    private bool _syncing;
    private string? _logoPath;
    private Bitmap? _logo;

    public BrandingStep(MainViewModel main)
        : base(main)
    {
        _logo = _defaultLogo;
        Themes = [.. ThemeInfo.All.Select(t => new ThemeCardViewModel(t))];
        Swatches = [.. new[] { "#1565c0", "#00838f", "#2e7d32", "#c2185b", "#d4481f", "#6a1b9a", "#ef6c00", "#37474f", "#0a7d5a", "#ff8f00" }.Select(Color.Parse)];
    }

    protected override string TitleText => "Branding";

    protected override string SubtitleText => "Logo, theme and brand colors, previewed in light and dark.";

    protected override string PitchText => "Seven built-in themes differ in more than colors: fonts, corner radius and density. With brand colors your app gets its own theme, based on the one you pick, as the default.";

    public override string Icon => "Icon.Palette";

    public IReadOnlyList<ThemeCardViewModel> Themes { get; }

    public IReadOnlyList<Color> Swatches { get; }

    public ThemeInfo Theme => ThemeInfo.Find(Main.Option("theme").Value);

    [ObservableProperty]
    public partial bool UseBrandColors { get; set; }

    public Color PrimaryColor
    {
        get => Parse(Main.Option("primaryColor").Value) ?? Theme.Light.Primary;
        set => Main.Option("primaryColor").Value = Hex(value);
    }

    public Color SecondaryColor
    {
        get => Parse(Main.Option("secondaryColor").Value) ?? Theme.Light.Secondary;
        set => Main.Option("secondaryColor").Value = Hex(value);
    }

    public string? LogoPath => Main.Option("logo") is { IsChanged: true, Value: { } path } ? path : null;

    public string LogoName => LogoPath is { } path ? Path.GetFileName(path) : L.Get("Coworkee logo (default)");

    public Bitmap? Logo
    {
        get
        {
            if (_logoPath != LogoPath)
            {
                _logoPath = LogoPath;
                _logo = LogoPath is null ? _defaultLogo : Images.TryLoad(LogoPath);
            }

            return _logo;
        }
    }

    public bool HasLogoPreview => Logo is not null;

    public PreviewViewModel PreviewLight => Preview(false);

    public PreviewViewModel PreviewDark => Preview(true);

    public override bool Owns(string error) =>
        error.StartsWith("branding.", StringComparison.Ordinal) || BrandIds.Any(id => error.StartsWith(id + ":", StringComparison.Ordinal));

    [RelayCommand]
    public void SelectTheme(ThemeCardViewModel card) => Main.Option("theme").Value = card.Theme.Name;

    [RelayCommand]
    public void UsePrimary(Color color)
    {
        UseBrandColors = true;
        PrimaryColor = color;
    }

    [RelayCommand]
    public async Task BrowseLogoAsync()
    {
        if (await Main.Dialogs.OpenFile(L.Get("Choose a logo"), ["*.svg", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.ico"]) is { } path)
        {
            Main.Option("logo").Value = path;
        }
    }

    [RelayCommand]
    public void ResetLogo() => Main.Option("logo").Reset();

    public void Sync()
    {
        _syncing = true;
        UseBrandColors = Main.Option("primaryColor").Value is not null || Main.Option("secondaryColor").Value is not null;
        _syncing = false;
    }

    public override void Refresh()
    {
        foreach (var card in Themes)
        {
            card.IsSelected = card.Theme == Theme;
        }

        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(PrimaryColor));
        OnPropertyChanged(nameof(SecondaryColor));
        OnPropertyChanged(nameof(LogoPath));
        OnPropertyChanged(nameof(LogoName));
        OnPropertyChanged(nameof(Logo));
        OnPropertyChanged(nameof(HasLogoPreview));
        OnPropertyChanged(nameof(PreviewLight));
        OnPropertyChanged(nameof(PreviewDark));
    }

    partial void OnUseBrandColorsChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }

        Main.Option("primaryColor").Value = value ? Hex(Theme.Light.Primary) : null;
        Main.Option("secondaryColor").Value = value ? Hex(Theme.Light.Secondary) : null;
    }

    private PreviewViewModel Preview(bool dark)
    {
        var palette = dark ? Theme.Dark : Theme.Light;
        if (UseBrandColors)
        {
            palette = palette.WithBrand(Parse(Main.Option("primaryColor").Value), Parse(Main.Option("secondaryColor").Value));
        }

        return new PreviewViewModel(palette, Theme.Radius, Theme.Font, Main.AppTitle, Logo, dark);
    }

    private static Color? Parse(string? hex) => hex is not null && Color.TryParse(hex, out var color) ? color : null;

    private static string Hex(Color color) => $"#{color.R:x2}{color.G:x2}{color.B:x2}";
}
