using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>Start from a template: the starter specs of the gallery, searchable and filtered by tag; picking one fills the wizard.</summary>
public sealed partial class GalleryViewModel(MainViewModel main) : ObservableObject
{
    public MainViewModel Main => main;

    public static IReadOnlyList<string> AllTags { get; } = [.. SpecGallery.All.SelectMany(e => e.Tags).Distinct().Order()];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Items), nameof(IsEmpty))]
    public partial string? Search { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Items), nameof(IsEmpty))]
    public partial string? Tag { get; set; }

    public IReadOnlyList<string> Tags => AllTags;

    public IReadOnlyList<GalleryItem> Items =>
    [
        .. SpecGallery.All.Select(e => new GalleryItem(e))
            .Where(i => Tag is null || i.Entry.Tags.Contains(Tag))
            .Where(i => string.IsNullOrWhiteSpace(Search) || Matches(i, Search.Trim())),
    ];

    public bool IsEmpty => Items.Count == 0;

    [RelayCommand]
    public void Pick(GalleryItem item)
    {
        main.NewApp();
        main.Load(SpecGallery.Read(item.Entry.Id), null);
        foreach (var step in main.Steps.Take(main.Steps.Count - 1))
        {
            step.IsReachable = true;
        }

        main.UpdateStates();
        main.Status = L.Format("Started from {0}", item.Title);
    }

    public void RefreshTexts() => OnPropertyChanged(nameof(Items));

    private static bool Matches(GalleryItem item, string search) =>
        new[] { item.Title, item.Description, item.Entry.Title, item.Entry.Id }.Concat(item.Entry.Tags).Any(t => t.Contains(search, StringComparison.CurrentCultureIgnoreCase));
}
