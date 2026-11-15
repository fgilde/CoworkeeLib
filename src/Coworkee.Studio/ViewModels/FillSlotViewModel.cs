using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

/// <summary>
/// One background of the sign-in pages: the theme's, an image or video (option <paramref name="fileId"/>), a color or a gradient (option <paramref name="fillId"/>).
/// Only one of them is set at a time.
/// </summary>
public sealed partial class FillSlotViewModel(MainViewModel main, string fileId, string fillId, Func<Palette> palette) : ObservableObject
{
    public const string Default = "Theme default";
    public const string File = "Image or video";
    public const string Color = "Color";
    public const string Gradient = "Gradient";

    private static readonly string[] AllModes = [Default, File, Color, Gradient];
    private string? _imagePath;
    private Bitmap? _image;

    /// <summary>The label of the layout (Page background, Form side, Poster, ...), English.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string Label { get; set; } = "Page background";

    public string Title => L.Get(Label);

    /// <summary>False where only a file makes sense (the media of the loading screen with a layout without a second background).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Modes))]
    public partial bool AllowsFill { get; set; } = true;

    public IReadOnlyList<string> Modes => AllowsFill ? AllModes : [Default, File];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFile), nameof(IsColor), nameof(IsGradient))]
    public partial string Mode { get; set; } = Default;

    public bool IsFile => Mode == File;

    public bool IsColor => Mode == Color;

    public bool IsGradient => Mode == Gradient;

    [ObservableProperty]
    public partial GradientViewModel? GradientEditor { get; set; }

    public string? FilePath
    {
        get => main.Option(fileId).Value;
        set => main.Option(fileId).Value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public bool HasFile => FilePath is not null;

    public bool IsVideo => Images.IsVideo(FilePath);

    public Bitmap? Image
    {
        get
        {
            if (_imagePath != FilePath)
            {
                _imagePath = FilePath;
                _image = Images.TryLoad(FilePath);
            }

            return _image;
        }
    }

    public SignInFillSpec? Fill => SignInStyle.Parse(main.Option(fillId).Value);

    /// <summary>The color of the color mode, as the picker shows it.</summary>
    public Avalonia.Media.Color ColorValue
    {
        get => FillBrush.Resolve(Fill?.Color, palette());
        set => ColorText = FillBrush.Hex(value);
    }

    /// <summary>The color as the spec stores it: hex, rgb()/hsl() or a theme color.</summary>
    public string? ColorText
    {
        get => Fill?.Color;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && SignInStyle.IsColor(value.Trim()))
            {
                main.Option(fillId).Value = SignInStyle.Format(new SignInFillSpec { Color = value.Trim() });
            }
        }
    }

    /// <summary>The fill as the sketches draw it; null keeps the theme's look.</summary>
    public IBrush? Brush => FillBrush.Create(Fill, palette());

    /// <summary>Takes over the options (a loaded spec): the mode follows what is set.</summary>
    public void Sync()
    {
        var fill = Fill;
        Mode = FilePath is not null ? File : fill?.Gradient is not null ? Gradient : fill?.Color is not null ? Color : Default;
        UseGradient(fill?.Gradient);
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(IsVideo));
        OnPropertyChanged(nameof(Image));
        OnPropertyChanged(nameof(ColorValue));
        OnPropertyChanged(nameof(ColorText));
        OnPropertyChanged(nameof(Brush));
    }

    [RelayCommand]
    public async Task BrowseAsync()
    {
        if (await main.Dialogs.OpenFile(L.Get("Choose an image or video"), ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.svg", "*.mp4", "*.webm", "*.ogg"]) is { } path)
        {
            FilePath = path;
        }
    }

    [RelayCommand]
    public void ClearFile() => FilePath = null;

    // switching clears what the other modes set, so the spec holds one background per slot
    partial void OnModeChanged(string value)
    {
        if (value != File)
        {
            FilePath = null;
        }

        var fill = Fill;
        switch (value)
        {
            case Color when fill?.Color is null:
                main.Option(fillId).Value = FillBrush.Hex(palette().Primary);
                break;
            case Gradient when fill?.Gradient is null:
                var gradient = GradientViewModel.Default(FillBrush.Hex(palette().Primary), FillBrush.Hex(palette().Secondary));
                main.Option(fillId).Value = SignInStyle.Format(new SignInFillSpec { Gradient = gradient });
                UseGradient(gradient);
                break;
            case Default or File:
                main.Option(fillId).Value = null;
                break;
        }
    }

    private void UseGradient(SignInGradientSpec? spec)
    {
        GradientEditor = spec is null ? null : new GradientViewModel(spec, text => FillBrush.Resolve(text, palette()));
        if (GradientEditor is { } editor)
        {
            editor.Changed += () => main.Option(fillId).Value = SignInStyle.Format(new SignInFillSpec { Gradient = editor.Spec() });
        }
    }
}
