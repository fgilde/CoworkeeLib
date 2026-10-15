namespace Coworkee.Studio.Model;

/// <summary>The icon of each option of the catalog (resource keys of Styles/Icons.axaml, Material Design Icons) shown on its card.</summary>
public static class OptionLooks
{
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["title"] = "Icon.FormatTitle",
        ["samples"] = "Icon.School",
        ["tests"] = "Icon.TestTube",
        ["git"] = "Icon.Git",
        ["airworks"] = "Icon.MonitorMultiple",
        ["airworksPackages"] = "Icon.Package",
        ["repository"] = "Icon.Github",
        ["auditing"] = "Icon.History",
        ["notifications"] = "Icon.Bell",
        ["chat"] = "Icon.Chat",
        ["reports"] = "Icon.ChartBox",
        ["ai"] = "Icon.Robot",
        ["backup"] = "Icon.BackupRestore",
        ["files"] = "Icon.Folder",
        ["features"] = "Icon.ToggleSwitch",
        ["social"] = "Icon.Forum",
        ["webhooks"] = "Icon.Webhook",
        ["extendedAttributes"] = "Icon.TagPlus",
        ["elasticsearch"] = "Icon.DatabaseSearch",
        ["eventBus"] = "Icon.Broadcast",
        ["clientEntities"] = "Icon.DatabaseSync",
        ["keycloak"] = "Icon.ShieldKey",
        ["storage"] = "Icon.Harddisk",
        ["aiProvider"] = "Icon.Sparkle",
        ["aiModel"] = "Icon.Brain",
        ["aiEndpoint"] = "Icon.Link",
        ["redis"] = "Icon.Memory",
        ["grafana"] = "Icon.ChartLine",
        ["webDataStudio"] = "Icon.DatabaseEye",
        ["aspireUI"] = "Icon.Apps",
        ["database"] = "Icon.Database",
        ["theme"] = "Icon.Palette",
        ["primaryColor"] = "Icon.FormatColorFill",
        ["secondaryColor"] = "Icon.InvertColors",
        ["logo"] = "Icon.Image",
        ["signInLayout"] = "Icon.PageLayoutSidebarLeft",
        ["signInMedia"] = "Icon.VideoImage",
        ["signInMediaSide"] = "Icon.FlipHorizontal",
        ["loadingScreen"] = "Icon.VideoImage",
        ["mapping"] = "Icon.SwapHorizontal",
        ["languages"] = "Icon.Translate",
        ["defaultLanguage"] = "Icon.Web",
        ["translationProvider"] = "Icon.Translate",
        ["translationSource"] = "Icon.Translate",
        ["colorMode"] = "Icon.ThemeLightDark",
        ["registration"] = "Icon.AccountPlus",
        ["githubActions"] = "Icon.CogPlay",
        ["devcontainer"] = "Icon.Container",
    };

    public static IReadOnlyCollection<string> Ids => Icons.Keys;

    public static string Icon(string id) => Icons.GetValueOrDefault(id, "Icon.Cube");
}
