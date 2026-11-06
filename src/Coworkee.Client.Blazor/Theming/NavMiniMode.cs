namespace Coworkee.Client.Blazor.Theming;

/// <summary>How the pinned, narrow navigation (mini mode) shows its entries.</summary>
public enum NavMiniMode
{
    /// <summary>Every link as an icon, the groups separated by lines (<see cref="CoworkeeTheme.NavMiniDividers"/>).</summary>
    Links,

    /// <summary>Only the first level; a click on a group opens its links next to it.</summary>
    GroupsOnClick,

    /// <summary>Only the first level; pointing at a group opens its links next to it.</summary>
    GroupsOnHover,
}
