using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

/// <summary>A newer Coworkee Studio on nuget.org; the update runs after the Studio has closed (Windows locks the files of a running tool) and starts it again.</summary>
public sealed partial class UpdateViewModel(MainViewModel main) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(Text))]
    public partial string? Available { get; set; }

    public bool HasUpdate => Available is not null;

    public string Text => Available is null ? string.Empty : L.Format("Update available {0}", Available);

    /// <summary>Asks nuget.org; no network, no badge.</summary>
    public async Task CheckAsync(Func<CancellationToken, Task<string[]>> versions, CancellationToken cancellationToken = default)
    {
        try
        {
            Available = ToolUpdates.Newer(await versions(cancellationToken), main.Version);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Available = null;
        }
    }

    /// <summary>Saves an opened spec, so the restarted Studio opens it again, then closes for the update of the CLI and the Studio.</summary>
    [RelayCommand]
    public void Install()
    {
        if (Available is not { } version)
        {
            return;
        }

        if (main.SpecPath is { } spec)
        {
            main.Save(spec);
        }

        string[] relaunch = main.SpecPath is { } path ? [ToolUpdates.ToolPath(ToolUpdates.StudioCommand), "--spec", path] : [ToolUpdates.ToolPath(ToolUpdates.StudioCommand)];
        try
        {
            main.Dialogs.Restart(ToolUpdates.Helper(Environment.ProcessId,
                [ToolUpdates.UpdateArgs(ToolUpdates.Cli, version), ToolUpdates.UpdateArgs(ToolUpdates.Studio, version)], relaunch));
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            main.Status = L.Format("The update could not start: {0}", exception.Message);
        }
    }
}
