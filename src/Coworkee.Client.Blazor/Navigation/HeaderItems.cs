using System.Text.Json;
using System.Text.Json.Serialization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Navigation;

/// <summary>The items of the app bar with their ids, and how the administrator's setting (<see cref="Contracts.Settings.HeaderSettings.Items"/>) arranges them.</summary>
public static class HeaderItems
{
    public const string Search = "search";
    public const string Language = "language";
    public const string Theme = "theme";
    public const string RightToLeft = "rtl";
    public const string DarkMode = "dark-mode";
    public const string Notifications = "notifications";
    public const string Role = "role";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string About = "about";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // app items keep their own small orders and sit between the search and the language menu, as before the header could be arranged
    public static IReadOnlyList<HeaderItem> BuiltIn { get; } =
    [
        new(Search, "Search", Icons.Material.Outlined.Search, -10_000),
        new(Language, "Language", Icons.Material.Outlined.Language, 10_000),
        new(Theme, "Theme", Icons.Material.Outlined.Palette, 10_100),
        new(RightToLeft, "Right to left", Icons.Material.Outlined.FormatTextdirectionRToL, 10_200),
        new(DarkMode, "Dark mode", Icons.Material.Outlined.DarkMode, 10_300),
        new(Notifications, "Notifications", Icons.Material.Outlined.Notifications, 10_400),
        new(Role, "Act as", Icons.Material.Outlined.Shield, 10_450),
        new(User, "Account", Icons.Material.Outlined.AccountCircle, 10_500),
        new(Assistant, "Assistant", Icons.Material.Outlined.AutoAwesome, 10_600),
        new(About, "About", Icons.Material.Outlined.Info, 10_700),
    ];

    /// <summary>On phones these leave the bar for the overflow menu.</summary>
    public static IReadOnlySet<string> FoldOnPhones { get; } = new HashSet<string>(StringComparer.Ordinal) { Language, Theme, RightToLeft, DarkMode, About };

    public static IReadOnlyList<HeaderItem> All(IEnumerable<IAppBarContributor> contributors)
    {
        var ids = new HashSet<string>(BuiltIn.Select(i => i.Id), StringComparer.Ordinal);
        var items = BuiltIn.ToList();
        foreach (var app in contributors.SelectMany(c => c.Items))
        {
            // the same component twice (other parameters) needs two keys
            var id = app.Id;
            for (var n = 2; !ids.Add(id); n++)
            {
                id = $"{app.Id}-{n}";
            }

            items.Add(new HeaderItem(id, app.Title, Icons.Material.Outlined.Extension, app.Order, app));
        }

        return items;
    }

    /// <summary>
    /// The configured items in their configured order; items the setting does not name follow the item before them in the default order,
    /// names of items that are gone are ignored. The account menu stays in the bar: without it nobody signs out.
    /// </summary>
    public static IReadOnlyList<(HeaderItem Item, HeaderPlacement Placement)> Arrange(IReadOnlyList<HeaderItem> available, IReadOnlyList<HeaderItemSetting> configured)
    {
        var defaults = available.OrderBy(i => i.Order).ToList();
        var byId = defaults.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var arranged = configured.Where(c => byId.ContainsKey(c.Id)).DistinctBy(c => c.Id).Select(c => (Item: byId[c.Id], c.Placement)).ToList();
        for (var i = 0; i < defaults.Count; i++)
        {
            if (arranged.Exists(a => a.Item.Id == defaults[i].Id))
            {
                continue;
            }

            var at = 0;
            for (var before = i - 1; before >= 0 && at == 0; before--)
            {
                at = arranged.FindIndex(a => a.Item.Id == defaults[before].Id) + 1;
            }

            arranged.Insert(at, (defaults[i], HeaderPlacement.Bar));
        }

        return arranged.Select(a => a.Item.Id == User ? (a.Item, HeaderPlacement.Bar) : a).ToList();
    }

    public static IReadOnlyList<HeaderItemSetting> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<HeaderItemSetting>>(json, Json)?.Where(s => !string.IsNullOrWhiteSpace(s?.Id)).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Serialize(IEnumerable<HeaderItemSetting> items) => JsonSerializer.Serialize(items, Json);
}
