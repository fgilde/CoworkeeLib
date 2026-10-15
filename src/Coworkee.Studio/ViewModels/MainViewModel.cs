using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly string? _settingsFile;
    private bool _loading = true;

    public MainViewModel(StudioSettings settings, Generator generator, Dialogs? dialogs = null, string? settingsFile = null)
    {
        Settings = settings;
        Generator = generator;
        Dialogs = dialogs ?? new Dialogs();
        _settingsFile = settingsFile;
        Options = OptionCatalog.All.ToDictionary(o => o.Id, o => new OptionViewModel(o) { Open = url => Dialogs.Open(url) });
        foreach (var option in Options.Values)
        {
            option.Changed += SpecChanged;
        }

        Welcome = new WelcomeStep(this);
        Basics = new BasicsStep(this);
        Branding = new BrandingStep(this);
        SignIn = new SignInStep(this);
        Architecture = new ArchitectureStep(this);
        Languages = new LanguagesStep(this);
        Entities = new EntitiesStep(this);
        Users = new UsersStep(this);
        Review = new ReviewStep(this);
        Generate = new GenerateStep(this);
        Update = new UpdateViewModel(this);
        Steps = [Welcome, Basics, Branding, SignIn, Architecture, Languages, Entities, Users, Review, Generate];
        Current = Welcome;
        _loading = false;
        UpdateStates();
        SpecChanged();
    }

    public event Action? LanguageChanged;

    public StudioSettings Settings { get; }

    public Generator Generator { get; }

    public Dialogs Dialogs { get; }

    public IReadOnlyDictionary<string, OptionViewModel> Options { get; }

    /// <summary>The key of the AI provider: never in the spec or the settings, only written to the user secrets of the created app host.</summary>
    public string? AiApiKey { get; set; }

    public WelcomeStep Welcome { get; }

    public BasicsStep Basics { get; }

    public BrandingStep Branding { get; }

    public SignInStep SignIn { get; }

    public ArchitectureStep Architecture { get; }

    public LanguagesStep Languages { get; }

    public EntitiesStep Entities { get; }

    public UsersStep Users { get; }

    public ReviewStep Review { get; }

    public GenerateStep Generate { get; }

    public UpdateViewModel Update { get; }

    public IReadOnlyList<StepViewModel> Steps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentIndex), nameof(IsWelcome), nameof(CanGoBack), nameof(CanGoNext), nameof(NextText), nameof(ProgressText), nameof(Progress), nameof(ShowFooter))]
    public partial StepViewModel Current { get; set; }

    [ObservableProperty]
    public partial bool IsForward { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors), nameof(ErrorSummary))]
    public partial IReadOnlyList<string> Errors { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpecName), nameof(HasSpecPath))]
    public partial string? SpecPath { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial bool IsHelpOpen { get; set; }

    [ObservableProperty]
    public partial int HelpTab { get; set; }

    public int CurrentIndex => Steps.ToList().IndexOf(Current);

    public bool IsWelcome => Current == Welcome;

    public bool ShowFooter => Current != Welcome;

    public bool CanGoBack => CurrentIndex > 0 && !Generate.IsRunning;

    public bool CanGoNext => CurrentIndex < Steps.Count - 1 && Current.CanContinue && !Generate.IsRunning;

    public string NextText => L.Get(Current == Review ? "Create the app" : "Next");

    public string ProgressText => L.Format("Step {0} of {1}", CurrentIndex + 1, Steps.Count);

    public double Progress => Steps.Count < 2 ? 0 : 100d * CurrentIndex / (Steps.Count - 1);

    public bool HasErrors => Errors.Count > 0;

    public string ErrorSummary => Errors.Count == 0 ? L.Get("Ready to create") : L.Format("{0} problems to fix", Errors.Count);

    public string AppTitle => Options["title"].Value is { Length: > 0 } title ? title : Basics.Name is { Length: > 0 } name ? name : L.Get("New app");

    public string? SpecName => SpecPath is null ? null : Path.GetFileName(SpecPath);

    public bool HasSpecPath => SpecPath is not null;

    public string Version { get; } = typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";

    public string ThemeMode => Settings.Theme is { Length: > 0 } theme ? theme : "System";

    public string ThemeModeText => L.Get(ThemeMode switch { "Light" => "Light", "Dark" => "Dark", _ => "Follow the system" });

    public int LanguageIndex
    {
        get => Settings.Language switch { "en" => 1, "de" => 2, _ => 0 };
        set => SetLanguage(value switch { 1 => "en", 2 => "de", _ => null });
    }

    public string LanguageCode => L.IsGerman ? "DE" : "EN";

    public string? CliPath
    {
        get => Settings.CliPath;
        set => UpdateSetting(() => Settings.CliPath = Blank(value));
    }

    public string? TemplateSource
    {
        get => Settings.TemplateSource;
        set => UpdateSetting(() => Settings.TemplateSource = Blank(value));
    }

    public string? PackageFeed
    {
        get => Settings.PackageFeed;
        set => UpdateSetting(() => Settings.PackageFeed = Blank(value));
    }

    public OptionViewModel Option(string id) => Options[id];

    [RelayCommand]
    public void Next()
    {
        if (Current == Review)
        {
            Generate.IsReachable = true;
            GoTo(Generate);
            Generate.StartCommand.Execute(null);
        }
        else if (CanGoNext)
        {
            Steps[CurrentIndex + 1].IsReachable = true;
            GoTo(Steps[CurrentIndex + 1]);
        }
    }

    [RelayCommand]
    public void Back()
    {
        if (CanGoBack)
        {
            GoTo(Steps[CurrentIndex - 1]);
        }
    }

    [RelayCommand]
    public void GoTo(StepViewModel step)
    {
        if (!step.IsReachable || step == Current || Generate.IsRunning)
        {
            return;
        }

        IsForward = Steps.ToList().IndexOf(step) > CurrentIndex;
        Current = step;
        UpdateStates();
        step.OnShown();
    }

    [RelayCommand]
    public void NewApp()
    {
        Load(new AppSpec(), null);
        SpecPath = null;
        AiApiKey = null;
        foreach (var step in Steps)
        {
            step.IsReachable = false;
        }

        Generate.Reset();
        Basics.IsReachable = true;
        GoTo(Basics);
    }

    [RelayCommand]
    public void Continue()
    {
        Basics.IsReachable = true;
        GoTo(Basics);
    }

    [RelayCommand]
    public async Task OpenSpecAsync()
    {
        if (await Dialogs.OpenFile(L.Get("Open an app spec"), ["*.json"]) is { } path)
        {
            Open(path);
        }
    }

    [RelayCommand]
    public void Open(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            Load(SpecJson.Read(File.ReadAllText(full)), full);
            SpecPath = full;
            Settings.AddRecent(full);
            SaveSettings();
            Welcome.Refresh();
            Status = L.Format("Opened {0}", Path.GetFileName(full));
            foreach (var step in Steps.Take(Steps.Count - 1))
            {
                step.IsReachable = true;
            }

            Generate.Reset();
            GoTo(Review);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Status = L.Format("Cannot open {0}: {1}", path, exception.Message);
        }
    }

    [RelayCommand]
    public async Task SaveSpecAsync()
    {
        if ((SpecPath ?? await Dialogs.SaveFile($"{(Basics.Name is { Length: > 0 } name ? name : "app")}.json")) is { } path)
        {
            Save(path);
        }
    }

    [RelayCommand]
    public async Task SaveSpecAsAsync()
    {
        if (await Dialogs.SaveFile(SpecName ?? $"{(Basics.Name is { Length: > 0 } name ? name : "app")}.json") is { } path)
        {
            Save(path);
        }
    }

    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full)!;
        CopyIntoFolder(folder);
        File.WriteAllText(full, SpecJson.Write(ToSpec(folder)));
        SpecPath = full;
        Settings.AddRecent(full);
        SaveSettings();
        Welcome.Refresh();
        Status = L.Format("Saved {0}", Path.GetFileName(full));
    }

    [RelayCommand]
    public void ToggleTheme()
    {
        Settings.Theme = ThemeMode switch { "System" => "Light", "Light" => "Dark", _ => null };
        SaveSettings();
        ApplyTheme();
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(ThemeModeText));
    }

    public void ApplyTheme()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = ThemeMode switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
        }
    }

    [RelayCommand]
    public void ToggleLanguage() => SetLanguage(L.IsGerman ? "en" : "de");

    [RelayCommand]
    public void ShowHelp(string? tab)
    {
        HelpTab = int.TryParse(tab, out var index) ? index : HelpTab;
        IsHelpOpen = true;
    }

    [RelayCommand]
    public void ToggleHelp() => IsHelpOpen = !IsHelpOpen;

    [RelayCommand]
    public async Task CopyAsync(string? text)
    {
        if (text is not null)
        {
            await Dialogs.Copy(text);
            Status = L.Get("Copied to the clipboard");
        }
    }

    [RelayCommand]
    public void OpenLink(string url) => Dialogs.Open(url);

    [RelayCommand]
    public async Task BrowseCliAsync()
    {
        if (await Dialogs.OpenFile(L.Get("Choose the coworkee CLI"), ["*"]) is { } path)
        {
            CliPath = path;
            OnPropertyChanged(nameof(CliPath));
        }
    }

    [RelayCommand]
    public async Task BrowseTemplateAsync()
    {
        if (await Dialogs.OpenFile(L.Get("Choose the Coworkee.Templates package"), ["*.nupkg"]) is { } path)
        {
            TemplateSource = path;
            OnPropertyChanged(nameof(TemplateSource));
        }
    }

    [RelayCommand]
    public async Task BrowseFeedAsync()
    {
        if (await Dialogs.PickFolder(L.Get("Folder with local packages")) is { } path)
        {
            PackageFeed = path;
            OnPropertyChanged(nameof(PackageFeed));
        }
    }

    /// <summary>The spec of everything entered; with <paramref name="relativeTo"/> paths below that folder become relative, like in a saved spec.</summary>
    public AppSpec ToSpec(string? relativeTo = null)
    {
        string? Relative(string? path)
        {
            if (relativeTo is null || path is null || !Path.IsPathRooted(path))
            {
                return path;
            }

            var relative = Path.GetRelativePath(relativeTo, path);
            return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative.Replace('\\', '/');
        }

        // a saved spec keeps the output folder only below its own folder; the Studio passes it as -o anyway
        var output = Relative(Basics.Output);
        var spec = new AppSpec { Name = Basics.Name.Trim(), Output = relativeTo is not null && output is not null && Path.IsPathRooted(output) ? null : output };
        foreach (var option in Options.Values.Where(o => o.Option.Group is not (OptionCatalog.Branding or OptionCatalog.SignIn) && o.Id != "title" && o.IsRelevant))
        {
            if (option is { IsChanged: true, Value: not null })
            {
                spec.Options[option.Id] = option.IsBool ? JsonValue.Create(option.IsOn) : JsonValue.Create(option.Value);
            }
        }

        var branding = new BrandingSpec
        {
            Title = Changed("title"),
            Theme = Changed("theme"),
            PrimaryColor = Changed("primaryColor"),
            SecondaryColor = Changed("secondaryColor"),
            Logo = Relative(Changed("logo")),
            SignInLayout = Changed("signInLayout"),
            SignInMedia = Relative(Changed("signInMedia")),
            SignInMediaSide = Changed("signInMediaSide"),
            LoadingScreen = Changed("loadingScreen"),
        };
        spec.Branding = branding is { Title: null, Theme: null, PrimaryColor: null, SecondaryColor: null, Logo: null, SignInLayout: null, SignInMedia: null, SignInMediaSide: null, LoadingScreen: null }
            ? null
            : branding;
        spec.Modules = Entities.ToSpec();
        spec.Enums = Entities.EnumSpecs();
        spec.Roles = Users.RoleSpecs();
        spec.Users = Users.UserSpecs();
        ListModules(spec);
        return spec;
    }

    // a spec of the defaults would show no module at all: every module of the library is listed with its effective value, in catalog order
    private static void ListModules(AppSpec spec)
    {
        var values = SpecOptions.Resolve(spec);
        spec.Options = OptionCatalog.All
            .Where(o => spec.Options.ContainsKey(o.Id) || (o.Group == OptionCatalog.Modules && OptionCatalog.IsActive(o, values)))
            .ToDictionary(o => o.Id, o => spec.Options.TryGetValue(o.Id, out var set) ? set : o.Kind == OptionKind.Bool ? (JsonNode?)JsonValue.Create(values[o.Id] == "true") : JsonValue.Create(values[o.Id]));
    }

    /// <summary>Takes over a spec; relative paths are resolved against the folder of <paramref name="specFile"/>.</summary>
    public void Load(AppSpec spec, string? specFile)
    {
        _loading = true;
        var folder = specFile is null ? Environment.CurrentDirectory : Path.GetDirectoryName(Path.GetFullPath(specFile))!;
        var values = SpecOptions.Resolve(spec);
        foreach (var option in Options.Values)
        {
            var value = values[option.Id];
            option.Value = option.Option.Kind == OptionKind.File && value is not null && value != option.Option.Default && !SpecValidator.IsWebAddress(value)
                ? Path.GetFullPath(Path.Combine(folder, value))
                : value;
        }

        Basics.Name = spec.Name;
        Basics.Location = specFile is null && spec.Output is null
            ? Basics.DefaultLocation
            : Path.GetDirectoryName(Path.GetFullPath(Path.Combine(folder, string.IsNullOrEmpty(spec.Output) ? spec.Name : spec.Output))) ?? folder;
        Entities.Load(spec.Modules, spec.Enums);
        Users.Load(spec.Roles, spec.Users);
        _loading = false;
        Branding.Sync();
        SignIn.Sync();
        SpecChanged();
    }

    public void SpecChanged()
    {
        if (_loading)
        {
            return;
        }

        foreach (var option in Options.Values.Where(o => o.Option.DependsOn is not null))
        {
            option.IsRelevant = OptionCatalog.IsActive(option.Option, id => Options[id].Value);
        }

        Errors = Validate();
        foreach (var step in Steps)
        {
            step.Errors = [.. Errors.Where(step.Owns)];
            step.Refresh();
        }

        OnPropertyChanged(nameof(AppTitle));
        OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>
    /// The problems of the spec as the CLI sees it when the Studio creates the app: output folder, logo and media go on the command line,
    /// so they are trusted wherever they are.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var spec = ToSpec();
        spec.Output = null;
        var trusted = Options.Values.Where(o => o.Option.Kind == OptionKind.File && o.IsChanged && o.Value is not null).Select(o => o.Value!).ToList();
        return SpecValidator.Validate(spec, Environment.CurrentDirectory, trusted);
    }

    public void SaveSettings()
    {
        try
        {
            Settings.Save(_settingsFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = exception.Message;
        }
    }

    public void SetLanguage(string? language)
    {
        Settings.Language = language;
        L.Use(language);
        SaveSettings();
        foreach (var step in Steps)
        {
            step.RefreshTexts();
        }

        OnPropertyChanged(string.Empty);
        LanguageChanged?.Invoke();
    }

    public void UpdateStates()
    {
        var index = CurrentIndex;
        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            step.IsReachable |= i <= index;
            step.State = i == index ? StepState.Current : step.IsReachable && i < Steps.Count - 1 ? StepState.Done : StepState.Upcoming;
        }

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>A spec takes logo and media only from its own folder: files from elsewhere are copied next to it.</summary>
    private void CopyIntoFolder(string folder)
    {
        foreach (var option in Options.Values.Where(o => o.Option.Kind == OptionKind.File && o.IsChanged))
        {
            if (option.Value is { } file && Path.IsPathRooted(file) && File.Exists(file) && !SpecValidator.IsUnder(folder, file))
            {
                var copy = Path.Combine(folder, Path.GetFileName(file));
                File.Copy(file, copy, overwrite: true);
                option.Value = copy;
            }
        }
    }

    private string? Changed(string id) => Options[id] is { IsChanged: true, IsRelevant: true, Value: { Length: > 0 } value } ? value : null;

    private void UpdateSetting(Action change)
    {
        change();
        SaveSettings();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
