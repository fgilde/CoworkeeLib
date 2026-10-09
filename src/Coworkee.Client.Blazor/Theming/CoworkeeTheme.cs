using MudBlazor;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>A MudBlazor theme plus the app's own look: navigation, logo, density. Everything here is editable in the theme editor.</summary>
public class CoworkeeTheme : MudTheme
{
    public static CoworkeeTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#d4481f",
            Secondary = "#16140f",
            Background = "#f3efe6",
            Surface = "#fbf9f4",
            AppbarBackground = "#f3efe6",
            AppbarText = "#16140f",
            DrawerBackground = "#ebe5d8",
            TextPrimary = "#16140f",
            TextSecondary = "#4a463d",
            LinesDefault = "#d6cfbf",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#f06a43",
            Secondary = "#efe9dc",
            Background = "#14130f",
            Surface = "#1f1d18",
            AppbarBackground = "#14130f",
            AppbarText = "#efe9dc",
            DrawerBackground = "#1c1a15",
            TextPrimary = "#efe9dc",
            TextSecondary = "#bdb6a6",
            LinesDefault = "#34312a",
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "6px", DrawerWidthLeft = "300px" },
    };

    public bool ShowLogoInNav { get; set; } = true;

    public bool ShowLogoInAppBar { get; set; } = true;

    public bool ShowNavFilter { get; set; } = true;

    public bool CanPinNav { get; set; } = true;

    public bool CanChangeNavExpandMode { get; set; } = true;

    /// <summary>Only one navigation group open at a time, until the user chooses otherwise.</summary>
    public bool NavSingleExpand { get; set; }

    /// <summary>Compact tables, lists, inputs, menus and chips across the app.</summary>
    public bool Dense { get; set; } = true;

    public bool StripedTables { get; set; }

    /// <summary>Users can choose the theme.</summary>
    public bool IsPublished { get; set; }

    /// <summary>Plain SVG markup shown as logo instead of the app's.</summary>
    public string? LogoSvg { get; set; }

    public string? CustomCss { get; set; }
}
