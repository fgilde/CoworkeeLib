using System.Text.Json;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Ai;

internal sealed class AssistantNavigateAction(NavigationManager nav, IEnumerable<INavigationContributor> contributors, CoworkeeLocalizer l) : IAssistantClientAction
{
    public string Name => "navigate";

    public string Description => "Opens a page of the app by its address: one from the page list, or a detail page such as /catalog/products/42.";

    public JsonElement Parameters { get; } = AssistantArguments.Schema(
        """{"type":"object","properties":{"href":{"type":"string","description":"The address within the app, starting with /"}},"required":["href"]}""");

    public Task<AssistantActionResult> RunAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (AssistantArguments.String(arguments, "href") is not { } href || !IsLocal(href))
        {
            return Task.FromResult(new AssistantActionResult(false, l["This address cannot be opened: {0}", AssistantArguments.String(arguments, "href") ?? string.Empty]));
        }

        // pages outside the app (e.g. the workflow designer) need a full page load
        nav.NavigateTo(href, contributors.SelectMany(c => c.Items).Any(i => i.ForceLoad && i.Href == href));
        return Task.FromResult(new AssistantActionResult(true, l["Opened {0}", href]));
    }

    // only addresses of this app: no other sites, no protocol relative or backslash tricks
    private static bool IsLocal(string href) =>
        href.StartsWith('/') && !href.StartsWith("//", StringComparison.Ordinal) && !href.Contains('\\') && Uri.IsWellFormedUriString(href, UriKind.Relative);
}
