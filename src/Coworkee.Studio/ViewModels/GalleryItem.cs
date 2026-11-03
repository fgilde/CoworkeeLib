using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed record GalleryItem(GalleryEntry Entry)
{
    public string Title => Entry.TitleIn(L.IsGerman);

    public string Description => Entry.DescriptionIn(L.IsGerman);

    public string Tags => string.Join("  ", Entry.Tags.Select(t => "#" + t));

    public string Icon => Entry.Icon;
}
