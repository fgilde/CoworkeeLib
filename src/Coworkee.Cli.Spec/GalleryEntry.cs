namespace Coworkee.Cli.Spec;

/// <summary>A starter spec of the gallery (templates/specs): what it is for, its tags and an icon as 24x24 SVG path.</summary>
public sealed record GalleryEntry(string Id, string Title, string Description, IReadOnlyList<string> Tags, string Icon, GalleryText? De = null)
{
    public string TitleIn(bool german) => german && De is { } de ? de.Title : Title;

    public string DescriptionIn(bool german) => german && De is { } de ? de.Description : Description;
}
