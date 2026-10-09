using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.About;

/// <summary>Logo (or a monogram of the title), title, description and version of the app.</summary>
public partial class AboutHeader
{
    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    private string AppVersion => Options.AppVersion ?? (Assembly.GetEntryAssembly() is { } app ? AboutVersion.Of(app) : null) ?? "-";

    private string Monogram => string.Concat(Options.AppTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0])));
}
