using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

/// <summary>My apps: the apps created with the Studio and those found in chosen folders, with their status, start/stop and links.</summary>
public sealed partial class MyAppsViewModel : ObservableObject
{
    private const int SearchDepth = 3;
    private static readonly string[] Skipped = ["bin", "obj", "node_modules", ".git", ".vs", ".idea"];

    public MyAppsViewModel(MainViewModel main)
    {
        Main = main;
        Load();
    }

    public MainViewModel Main { get; }

    /// <summary>Runs an update of the UI on its thread; tests run it in place.</summary>
    public Action<Action> Dispatch { get; set; } = action => Dispatcher.UIThread.Post(action);

    public AppProbe Probe { get; set; } = new();

    public ObservableCollection<MyAppViewModel> Apps { get; } = [];

    public IReadOnlyList<string> Folders => [.. Main.Settings.AppFolders];

    public bool IsEmpty => Apps.Count == 0;

    /// <summary>Takes a created app into the list (or updates it) and saves the settings.</summary>
    public void Remember(string folder, AppSpec spec)
    {
        var full = Path.GetFullPath(folder);
        Main.Settings.Apps.RemoveAll(a => Same(a.Path, full));
        Main.Settings.Apps.Insert(0, new KnownApp(full, spec.Name, DateTimeOffset.Now, Differences(spec)));
        Main.SaveSettings();
        Load();
    }

    [RelayCommand]
    public async Task AddFolderAsync()
    {
        if (await Main.Dialogs.PickFolder(L.Get("Folder with your apps")) is { } folder)
        {
            AddFolder(folder);
        }
    }

    public void AddFolder(string folder)
    {
        var full = Path.GetFullPath(folder);
        if (!Main.Settings.AppFolders.Any(f => Same(f, full)))
        {
            Main.Settings.AppFolders.Add(full);
        }

        var found = Scan();
        Main.Status = L.Format("{0} apps found", found);
        OnPropertyChanged(nameof(Folders));
    }

    [RelayCommand]
    public void RemoveFolder(string folder)
    {
        Main.Settings.AppFolders.RemoveAll(f => Same(f, folder));
        Main.SaveSettings();
        OnPropertyChanged(nameof(Folders));
    }

    /// <summary>Looks through the folders for apps not in the list yet; returns how many apps the folders hold.</summary>
    public int Scan()
    {
        var found = Main.Settings.AppFolders.Where(Directory.Exists).SelectMany(f => Discover(f, SearchDepth)).ToList();
        foreach (var app in found.Where(f => !Main.Settings.Apps.Any(a => Same(a.Path, f.Path))))
        {
            Main.Settings.Apps.Add(app);
        }

        Main.SaveSettings();
        Load();
        return found.Count;
    }

    [RelayCommand]
    public void Forget(MyAppViewModel app)
    {
        Main.Settings.Apps.RemoveAll(a => Same(a.Path, app.Path));
        Main.SaveSettings();
        Apps.Remove(app);
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    public void Refresh()
    {
        foreach (var app in Apps)
        {
            app.UpdateStatus();
        }
    }

    /// <summary>Stops what the Studio started, when it closes.</summary>
    public void StopAll()
    {
        foreach (var app in Apps)
        {
            app.Run.Stop();
        }
    }

    /// <summary>An app is a folder with <c>.coworkee/app.json</c> (written by the CLI) or with an app host project in <c>src</c>.</summary>
    public static IEnumerable<KnownApp> Discover(string folder, int depth)
    {
        if (Read(folder) is { } app)
        {
            yield return app;
            yield break;
        }

        if (depth == 0)
        {
            yield break;
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(folder).Where(d => !Skipped.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var found in children.SelectMany(c => Discover(c, depth - 1)))
        {
            yield return found;
        }
    }

    private static KnownApp? Read(string folder)
    {
        var state = Path.Combine(folder, ".coworkee", "app.json");
        if (File.Exists(state))
        {
            try
            {
                var spec = SpecJson.Read(File.ReadAllText(state));
                return new KnownApp(folder, spec.Name, File.GetCreationTime(state), Differences(spec));
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
            {
                // fall back to the app host
            }
        }

        return AppProbe.ProcessName(folder) is { } appHost && Directory.GetFiles(folder, "*.slnx").Length > 0
            ? new KnownApp(folder, appHost[..^".AppHost".Length], Directory.GetCreationTime(folder), [])
            : null;
    }

    private static Dictionary<string, string?> Differences(AppSpec spec) =>
        SpecOptions.Resolve(spec).Where(o => OptionCatalog.Find(o.Key) is { } option && option.Default != o.Value && option.Kind is OptionKind.Bool or OptionKind.Choice)
            .ToDictionary(o => o.Key, o => o.Value);

    private void Load()
    {
        var running = Apps.ToDictionary(a => a.Path, StringComparer.OrdinalIgnoreCase);
        Apps.Clear();
        foreach (var app in Main.Settings.Apps.Where(a => Directory.Exists(a.Path)))
        {
            Apps.Add(running.TryGetValue(app.Path, out var existing) ? existing : new MyAppViewModel(this, app));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private static bool Same(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
}
