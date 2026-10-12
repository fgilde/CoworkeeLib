using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Coworkee.Studio.Views;

namespace Coworkee.Studio.Tests;

public sealed class WindowTests
{
    [Fact]
    public Task A_language_switch_rebuilds_the_view_in_the_new_language() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        main.GoTo(main.Basics);
        var window = new MainWindow { DataContext = main };
        window.Attach(main);
        window.Show();
        var before = window.Content;

        main.SetLanguage("de");
        Dispatcher.UIThread.RunJobs();

        window.Content.ShouldNotBeSameAs(before);
        window.GetVisualDescendants().OfType<TextBlock>().ShouldContain(t => t.Text == "Weiter");
        main.Settings.Language.ShouldBe("de");
        main.LanguageIndex.ShouldBe(2);
        main.LanguageCode.ShouldBe("DE");

        // the switch in the title bar
        main.ToggleLanguageCommand.Execute(null);
        main.Settings.Language.ShouldBe("en");
        main.LanguageCode.ShouldBe("EN");
        window.Close();
    });

    [Fact]
    public Task The_welcome_page_has_the_wide_panel_and_no_footer() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Hero").Classes.ShouldContain("wide");
        main.ShowFooter.ShouldBeFalse();

        main.NewApp();
        Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Hero").Classes.ShouldNotContain("wide");
        window.Close();
    });
}
