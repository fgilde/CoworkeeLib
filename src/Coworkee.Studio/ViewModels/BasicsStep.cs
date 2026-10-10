using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed partial class BasicsStep(MainViewModel main) : StepViewModel(main)
{
    protected override string TitleText => "Basics";

    protected override string SubtitleText => "Name, title and where the solution goes.";

    protected override string PitchText => "The name becomes the prefix of every project and namespace, the title is what your users read in the browser, the app bar and on the sign-in pages.";

    public override string Icon => "Icon.Card";

    public string DefaultLocation => Main.Settings.Location is { } location && Directory.Exists(location)
        ? location
        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Output), nameof(NameError), nameof(ProjectsPreview), nameof(TitlePlaceholder))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Output))]
    public partial string Location { get; set; } = main.Settings.Location is { } location && Directory.Exists(location)
        ? location
        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public OptionViewModel TitleOption => Main.Option("title");

    public string? Output => string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Location) ? null : Path.Combine(Location, Name.Trim());

    public bool OutputExists => Output is { } output && Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any();

    public string? NameError => Name.Length == 0
        ? L.Get("The app needs a name.")
        : SpecValidator.IsSimpleName(Name.Trim()) ? null : L.Get("Start with a letter, then letters or digits only; no spaces, dots or dashes.");

    public string TitlePlaceholder => Name is { Length: > 0 } ? Name : "Acme Shop";

    public string ProjectsPreview => SpecValidator.IsSimpleName(Name.Trim())
        ? $"{Name.Trim()}.Api · {Name.Trim()}.Web · {Name.Trim()}.AppHost · namespace {Name.Trim()}.*"
        : "MyApp.Api · MyApp.Web · MyApp.AppHost";

    public override bool CanContinue => NameError is null && TitleError is null && Output is not null;

    public string? TitleError => TitleOption.Value is { } title && !SpecValidator.IsSafeTitle(title) ? L.Get("No quotes, backslashes, <, >, @ or control characters: the title goes into code and HTML.") : null;

    public override bool Owns(string error) => error.StartsWith("name:", StringComparison.Ordinal) || error.StartsWith("title:", StringComparison.Ordinal);

    [RelayCommand]
    public async Task BrowseLocationAsync()
    {
        if (await Main.Dialogs.PickFolder(L.Get("Folder for the new app")) is { } folder)
        {
            Location = folder;
        }
    }

    public override void Refresh()
    {
        OnPropertyChanged(nameof(OutputExists));
        OnPropertyChanged(nameof(TitleError));
    }

    partial void OnNameChanged(string value)
    {
        Main.SpecChanged();
        Main.UpdateStates();
    }

    partial void OnLocationChanged(string value)
    {
        if (Directory.Exists(value))
        {
            Main.Settings.Location = value;
        }

        Main.SpecChanged();
        Main.UpdateStates();
    }
}
