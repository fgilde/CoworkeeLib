using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

/// <summary>
/// The GitHub deploy workflow of an app in a small dialog: its settings from .coworkee/app.json (or what the workflow file shows); saving runs
/// coworkee deploy azure --github, which writes the workflow and keeps the settings. A workflow changed by hand is overwritten only when the person says so.
/// </summary>
public sealed partial class DeployWorkflowViewModel(MainViewModel main, Func<string?> output, Action<Action> dispatch) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>Empty: the app's name.</summary>
    [ObservableProperty]
    public partial string Prefix { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Location { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Branches { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Teardown { get; set; }

    /// <summary>The workflow file is no longer what coworkee wrote.</summary>
    [ObservableProperty]
    public partial bool IsChangedByHand { get; set; }

    /// <summary>Saving asked whether to overwrite the changes made by hand.</summary>
    [ObservableProperty]
    public partial bool AskOverwrite { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(OverwriteCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int? ExitCode { get; set; }

    public ObservableCollection<string> Log { get; } = [];

    public string StatusText => ExitCode switch
    {
        null => L.Get("Every push to a listed branch deploys it to Azure Container Apps, into a resource group of its own (prefix-branch). The one-time setup in Azure and GitHub is in the docs (Hosting, Deployment)."),
        0 => L.Get("Workflow written: commit and push it."),
        var code => L.Format("coworkee deploy ended with exit code {0}", code),
    };

    [RelayCommand]
    public void Open()
    {
        if (output() is not { } folder)
        {
            return;
        }

        var settings = DeployWorkflowSettings.Of(ExistingApp.Read(folder).Spec);
        Prefix = settings.ResourceGroupPrefix ?? string.Empty;
        Location = settings.Location;
        Branches = string.Join(", ", settings.Branches);
        Teardown = settings.TeardownOnDelete;
        var file = Path.Combine(folder, DeployWorkflowSettings.RelativePath);
        IsChangedByHand = File.Exists(file) && DeployWorkflowSettings.IsChangedByHand(File.ReadAllText(file));
        AskOverwrite = false;
        ExitCode = null;
        Log.Clear();
        IsOpen = true;
    }

    [RelayCommand]
    public void Close() => IsOpen = false;

    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task SaveAsync()
    {
        if (IsChangedByHand)
        {
            AskOverwrite = true;
            return Task.CompletedTask;
        }

        return RunAsync(force: false);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task OverwriteAsync()
    {
        AskOverwrite = false;
        return RunAsync(force: true);
    }

    /// <summary>The workflow changed by hand stays as it is; nothing is written.</summary>
    [RelayCommand]
    public void Keep()
    {
        AskOverwrite = false;
        IsOpen = false;
    }

    public string[] Arguments(bool force) =>
    [
        "deploy", "azure", "--github", "--resource-group-prefix", Prefix.Trim(), "--location", Location.Trim(),
        "--branches", .. DeployWorkflowSettings.BranchList(Branches), "--teardown-on-delete", Teardown ? "true" : "false",
        .. force ? ["--force"] : Array.Empty<string>(),
    ];

    private async Task RunAsync(bool force)
    {
        if (output() is not { } folder)
        {
            return;
        }

        Log.Clear();
        ExitCode = null;
        IsBusy = true;
        try
        {
            var exit = await main.Generator.DeployWorkflowAsync(folder, Arguments(force), main.Settings, line => dispatch(() => Log.Add(line.Text)), CancellationToken.None);
            dispatch(() =>
            {
                ExitCode = exit;
                IsChangedByHand = IsChangedByHand && exit != 0;
            });
        }
        catch (Win32Exception exception)
        {
            dispatch(() => Log.Add(exception.Message));
        }
        finally
        {
            dispatch(() => IsBusy = false);
        }
    }

    private bool CanRun() => !IsBusy;
}
