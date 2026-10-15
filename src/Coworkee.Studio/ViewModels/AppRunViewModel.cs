using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public enum AppRunState
{
    Stopped,
    Starting,
    Running,
    Failed,
}

/// <summary>An app run from the Studio: dotnet run of its app host with the output, the dashboard login and the addresses of the app.</summary>
public sealed partial class AppRunViewModel(MainViewModel main, Func<string?> output, Action<Action> dispatch) : ObservableObject
{
    private const int MaxLines = 2000;
    private CancellationTokenSource? _cancel;
    private bool _failMessageNext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsStarting), nameof(IsRunning), nameof(IsFailed), nameof(StatusText))]
    public partial AppRunState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDashboard))]
    public partial string? DashboardUrl { get; set; }

    [ObservableProperty]
    public partial bool ShowLog { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<AppEndpoint> Endpoints { get; set; } = [];

    public ObservableCollection<string> Log { get; } = [];

    /// <summary>Started at least once: the card stays to show how it ended.</summary>
    [ObservableProperty]
    public partial bool WasStarted { get; set; }

    public bool IsBusy => State is AppRunState.Starting or AppRunState.Running;

    public bool IsStarting => State == AppRunState.Starting;

    public bool IsRunning => State == AppRunState.Running;

    public bool IsFailed => State == AppRunState.Failed;

    public bool HasDashboard => DashboardUrl is not null;

    public string StatusText => State switch
    {
        AppRunState.Starting => L.Get("Starting the app host…"),
        AppRunState.Running => L.Get("The app is running"),
        AppRunState.Failed => Error ?? L.Get("The app host stopped"),
        _ => L.Get("Stopped"),
    };

    [RelayCommand]
    public async Task StartAsync()
    {
        if (IsBusy || output() is not { } folder)
        {
            return;
        }

        var appHost = AspireOutput.AppHost(folder);
        Log.Clear();
        Error = null;
        DashboardUrl = null;
        _failMessageNext = false;
        WasStarted = true;
        if (appHost is null)
        {
            Fail(L.Format("No app host project below {0}", folder));
            return;
        }

        Endpoints = AspireOutput.Endpoints(folder);
        State = AppRunState.Starting;
        Add("> dotnet run --project " + appHost, false);
        _cancel = new CancellationTokenSource();
        try
        {
            var exit = await main.Generator.RunAppAsync(appHost, main.Settings, (line, error) => dispatch(() => Add(line, error)), _cancel.Token);
            dispatch(() =>
            {
                if (exit == 0)
                {
                    State = AppRunState.Stopped;
                }
                else
                {
                    Fail(Error ?? L.Format("The app host ended with exit code {0}", exit));
                }
            });
        }
        catch (OperationCanceledException)
        {
            dispatch(() => State = AppRunState.Stopped);
        }
        catch (Win32Exception exception)
        {
            dispatch(() => Fail(exception.Message));
        }
    }

    [RelayCommand]
    public void Stop() => _cancel?.Cancel();

    [RelayCommand]
    public void Open(string url) => main.Dialogs.Open(url);

    public void Add(string line, bool error)
    {
        if (Log.Count == MaxLines)
        {
            Log.RemoveAt(0);
        }

        Log.Add(line);
        if (AspireOutput.DashboardLogin(line) is { } login)
        {
            DashboardUrl = login;
        }
        else if (AspireOutput.IsStarted(line))
        {
            State = AppRunState.Running;
        }
        else if (_failMessageNext && line.Trim().Length > 0)
        {
            Error ??= line.Trim();
            _failMessageNext = false;
        }
        else if (AspireOutput.IsFailHeader(line))
        {
            _failMessageNext = true;
        }
        else if ((error || AspireOutput.IsError(line)) && line.Trim().Length > 0)
        {
            Error ??= line.Trim();
        }
    }

    private void Fail(string error)
    {
        Error = error;
        State = AppRunState.Failed;
        ShowLog = true;
    }
}
