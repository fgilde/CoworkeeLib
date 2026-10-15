using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

/// <summary>One app of My apps: started and stopped with the runner of the Create page, or found running outside the Studio.</summary>
public sealed partial class MyAppViewModel : ObservableObject
{
    private readonly MyAppsViewModel _owner;

    public MyAppViewModel(MyAppsViewModel owner, KnownApp app)
    {
        _owner = owner;
        App = app;
        Run = new AppRunViewModel(owner.Main, () => app.Path, action => owner.Dispatch(action));
        Run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppRunViewModel.State) or nameof(AppRunViewModel.DashboardUrl))
            {
                UpdateStatus();
            }
        };
        Run.Log.CollectionChanged += (_, _) => OnPropertyChanged(nameof(LastLog));
        UpdateStatus();
    }

    public KnownApp App { get; }

    public AppRunViewModel Run { get; }

    public string Name => App.Name;

    public string Path => App.Path;

    public string Created => L.Format("Created {0}", App.Created.LocalDateTime.ToString("d", CultureInfo.CurrentCulture));

    /// <summary>The options that differ from the template, e.g. "database sqlite · keycloak".</summary>
    public string OptionsText => string.Join(" · ", App.Options.Select(o => o.Value switch { "true" => o.Key, "false" => o.Key + " " + L.Get("off"), _ => $"{o.Key} {o.Value}" }));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsStopped), nameof(StatusText), nameof(Links))]
    public partial AppStatus Detected { get; set; }

    public bool IsRunning => Run.IsBusy || Detected == AppStatus.Running;

    public bool IsStopped => !IsRunning;

    public string StatusText => Run.IsBusy ? Run.StatusText
        : Detected == AppStatus.Running ? L.Get("Running outside the Studio")
        : Detected == AppStatus.PortTaken ? L.Get("Stopped; another program holds the dashboard port")
        : Run.IsFailed ? Run.StatusText
        : L.Get("Stopped");

    public string LastLog => Run.Log.Count > 0 ? Run.Log[^1] : string.Empty;

    /// <summary>Dashboard (with the login token once the Studio started it), the projects of the app and the tools that show up in the dashboard.</summary>
    public IReadOnlyList<AppEndpoint> Links
    {
        get
        {
            if (!IsRunning)
            {
                return [];
            }

            var dashboard = Run.DashboardUrl ?? AspireOutput.Dashboard(Path);
            var links = new List<AppEndpoint>();
            if (dashboard is not null)
            {
                links.Add(new AppEndpoint(L.Get("Dashboard"), dashboard));
            }

            links.AddRange(AspireOutput.Endpoints(Path));
            if (dashboard is not null)
            {
                links.AddRange(new[] { ("grafana", "Grafana"), ("webDataStudio", "WebDataStudio") }
                    .Where(t => On(t.Item1)).Select(t => new AppEndpoint(t.Item2, dashboard)));
            }

            return links;
        }
    }

    [RelayCommand]
    public void Start()
    {
        if (!IsRunning)
        {
            _ = Run.StartAsync();
        }
    }

    [RelayCommand]
    public void Stop()
    {
        if (Run.IsBusy)
        {
            Run.Stop();
        }
        else if (AppProbe.ProcessName(Path) is { } name)
        {
            _owner.Probe.Kill(name);
            UpdateStatus();
        }
    }

    [RelayCommand]
    public void Open(string url) => _owner.Main.Dialogs.Open(url);

    [RelayCommand]
    public void OpenFolder() => _owner.Main.Dialogs.Open(Path);

    /// <summary>The solution with the program the system opens .slnx files with (Visual Studio, Rider), else the folder.</summary>
    [RelayCommand]
    public void OpenSolution() => _owner.Main.Dialogs.Open(Directory.GetFiles(Path, "*.slnx").FirstOrDefault() ?? Path);

    [RelayCommand]
    public void OpenInCode()
    {
        try
        {
            Launch.Tool("code", Path, Path);
        }
        catch (Win32Exception exception)
        {
            _owner.Main.Status = exception.Message;
        }
    }

    public void UpdateStatus()
    {
        Detected = Run.IsBusy ? AppStatus.Stopped : _owner.Probe.Status(Path);
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Links));
    }

    // grafana and webDataStudio are on unless the app turned them off
    private bool On(string option) => !App.Options.TryGetValue(option, out var value) || value != "false";
}
