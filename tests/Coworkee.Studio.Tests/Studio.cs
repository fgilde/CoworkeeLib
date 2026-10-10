using System.Diagnostics;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Model;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

/// <summary>A view model on its own settings file, with a process runner the test controls.</summary>
internal static class Studio
{
    public static MainViewModel Create(TempFolder folder, ProcessRunner? runner = null, StudioSettings? settings = null)
    {
        Localization.L.Use("en");
        var main = new MainViewModel(settings ?? new StudioSettings { Location = folder.Path }, new Generator(runner ?? NotFound), new Dialogs(), folder.File("settings.json"));
        main.Generate.Dispatch = action => action();
        return main;
    }

    public static Task<int> NotFound(ProcessStartInfo start, Action<string, bool> output, CancellationToken cancellationToken) =>
        throw new System.ComponentModel.Win32Exception(2, "not found");
}
