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

        // the language menu of the title bar: the system's language and the eight languages, the chosen one checked
        var items = LanguageMenu(window);
        items.Count.ShouldBe(9);
        items.Single(i => i.IsChecked).Header.ShouldBe("Deutsch");
        var french = items.Single(i => (string?)i.Header == "Français");
        french.Command!.Execute(french.CommandParameter);
        main.Settings.Language.ShouldBe("fr");
        Localization.L.Language.ShouldBe("fr");
        main.LanguageCode.ShouldBe("FR");
        main.LanguageIndex.ShouldBe(3);

        // the rebuilt view has its own menu: back to the language of the system
        Dispatcher.UIThread.RunJobs();
        var system = LanguageMenu(window)[0];
        system.IsChecked.ShouldBeFalse();
        system.Command!.Execute(system.CommandParameter);
        main.Settings.Language.ShouldBeNull();
        main.LanguageSetting.ShouldBe("system");
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

    private static List<MenuItem> LanguageMenu(MainWindow window)
    {
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "LanguageButton");
        var menu = (MenuFlyout)button.Flyout!;
        menu.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        return menu.Items.OfType<MenuItem>().ToList();
    }
}
