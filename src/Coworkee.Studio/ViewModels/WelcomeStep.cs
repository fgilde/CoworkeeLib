using CommunityToolkit.Mvvm.ComponentModel;

namespace Coworkee.Studio.ViewModels;

public sealed record RecentSpec(string Path)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}

public sealed partial class WelcomeStep(MainViewModel main) : StepViewModel(main)
{
    protected override string TitleText => "Welcome";

    protected override string SubtitleText => "Configure a Coworkee app, see what every choice brings, and let the CLI create it.";

    protected override string PitchText => "Business apps without the boilerplate. Identity, permissions, an auth server, a Blazor shell, data tables, theming and an Aspire app host come as packages; your app only holds its own domain.";

    public override string Icon => "Icon.Rocket";

    [ObservableProperty]
    public partial IReadOnlyList<RecentSpec> Recent { get; set; } = [.. main.Settings.RecentSpecs.Where(File.Exists).Select(f => new RecentSpec(f))];

    public bool HasRecent => Recent.Count > 0;

    public override void Refresh()
    {
        Recent = [.. Main.Settings.RecentSpecs.Where(File.Exists).Select(f => new RecentSpec(f))];
        OnPropertyChanged(nameof(HasRecent));
    }
}
