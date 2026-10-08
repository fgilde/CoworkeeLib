using System.Reflection;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class AboutDialog
{
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;


    private string AppVersion => Options.AppVersion ?? (Assembly.GetEntryAssembly() is { } app ? Version(app) : "-");

    internal static string Version(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "-";
        return version.Split('+')[0];
    }

    private void Close() => Dialog.Close();
}
