using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

public sealed partial class LayoutCardViewModel(string value, string description) : ObservableObject
{
    public string Value => value;

    public string Title => L.Get(value);

    public string Description => L.Get(description);

    public bool IsCard => value == "Card";

    public bool IsSplit => value == "Split";

    public bool IsSlide => value == "Slide";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public sealed partial class SignInStep : StepViewModel
{
    private string? _mediaPath;
    private Bitmap? _media;

    public SignInStep(MainViewModel main)
        : base(main)
    {
        Layouts = [.. OptionCatalog.Find("signInLayout")!.Choices!.Select(c => new LayoutCardViewModel(c.Value, c.Description))];
    }

    protected override string TitleText => "Sign-in";

    protected override string SubtitleText => "How the sign-in, registration and password pages look.";

    protected override string PitchText => "The auth server brings sign-in, step-by-step registration, password reset and external logins. Pick a design now; administrators can switch it later on the Themes page.";

    public override string Icon => "Icon.Login";

    public IReadOnlyList<LayoutCardViewModel> Layouts { get; }

    public string Layout => Main.Option("signInLayout").Value ?? "Slide";

    public bool IsCard => Layout == "Card";

    public bool IsSplit => Layout == "Split";

    public bool IsSlide => Layout == "Slide";

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

    public string? MediaPath
    {
        get => Main.Option("signInMedia").Value;
        set => Main.Option("signInMedia").Value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public bool HasMedia => MediaPath is not null;

    public bool IsVideo => Images.IsVideo(MediaPath);

    public Bitmap? Media
    {
        get
        {
            if (_mediaPath != MediaPath)
            {
                _mediaPath = MediaPath;
                _media = Images.TryLoad(MediaPath);
            }

            return _media;
        }
    }

    public bool HasMediaPreview => Media is not null;

    public PreviewViewModel Preview => Main.Branding.PreviewLight;

    public override bool Owns(string error) => error.StartsWith("signIn", StringComparison.Ordinal);

    [RelayCommand]
    public void SelectLayout(LayoutCardViewModel card) => Main.Option("signInLayout").Value = card.Value;

    [RelayCommand]
    public async Task BrowseMediaAsync()
    {
        if (await Main.Dialogs.OpenFile(L.Get("Choose an image or video"), ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.svg", "*.mp4", "*.webm", "*.ogg"]) is { } path)
        {
            MediaPath = path;
        }
    }

    [RelayCommand]
    public void ClearMedia() => MediaPath = null;

    public void Sync() => Refresh();

    public override void Refresh()
    {
        foreach (var card in Layouts)
        {
            card.IsSelected = card.Value == Layout;
        }

        OnPropertyChanged(string.Empty);
    }
}
