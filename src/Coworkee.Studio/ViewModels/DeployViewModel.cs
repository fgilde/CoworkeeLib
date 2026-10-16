using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

/// <summary>The optional last step: coworkee deploy for a target, with its output live.</summary>
public sealed partial class DeployViewModel(MainViewModel main, Func<string?> output, Action<Action> dispatch) : ObservableObject
{
    private CancellationTokenSource? _cancel;

    public static IReadOnlyList<string> Targets { get; } = ["compose", "azure", "k8s"];

    [ObservableProperty]
    public partial string Target { get; set; } = "compose";

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int? ExitCode { get; set; }

    public ObservableCollection<string> Log { get; } = [];

    public string StatusText => IsBusy ? L.Get("Writing the deployment…")
        : ExitCode switch
        {
            null => L.Get("Docker Compose for one host, Azure Container Apps (Bicep) or Kubernetes (Helm). The tools of the app go along."),
            0 => L.Get("Done: the addresses and the sign-in are at the end of the output."),
            var code => L.Format("coworkee deploy ended with exit code {0}", code),
        };

    [RelayCommand]
    public void Open() => IsOpen = true;

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartAsync()
    {
        if (output() is not { } folder)
        {
            return;
        }

        Log.Clear();
        ExitCode = null;
        IsBusy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            var exit = await main.Generator.DeployAsync(folder, Target, main.Settings, line => dispatch(() => Log.Add(line.Text)), _cancel.Token);
            dispatch(() => ExitCode = exit);
        }
        catch (Exception exception) when (exception is OperationCanceledException or Win32Exception)
        {
            dispatch(() => Log.Add(exception.Message));
        }
        finally
        {
            dispatch(() => IsBusy = false);
        }
    }

    [RelayCommand]
    public void Stop() => _cancel?.Cancel();

    private bool CanStart() => !IsBusy;
}
