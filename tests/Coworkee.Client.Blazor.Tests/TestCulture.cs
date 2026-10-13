using System.Globalization;
using System.Runtime.CompilerServices;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>
/// Every layout switches the process-wide culture to English while other classes run, so a component built before the switch parsed
/// in the machine culture what its test typed in English: all threads start in English; tests switching language run alone and restore it.
/// </summary>
public sealed class TestCulture : IDisposable
{
    public void Dispose() => Pin();

    [ModuleInitializer]
    internal static void Pin()
    {
        var english = CultureInfo.GetCultureInfo(Localization.CoworkeeLocalizer.English);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = english;
    }
}
