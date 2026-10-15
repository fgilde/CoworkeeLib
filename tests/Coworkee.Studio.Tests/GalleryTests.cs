using Avalonia.Threading;
using Avalonia.VisualTree;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Views;

namespace Coworkee.Studio.Tests;

public sealed class GalleryTests
{
    [Fact]
    public void Search_and_tag_narrow_the_templates()
    {
        using var folder = new TempFolder();
        var gallery = Studio.Create(folder).Gallery;

        gallery.Items.Count.ShouldBe(SpecGallery.All.Count);
        gallery.Tags.ShouldContain("sales");

        gallery.Tag = "sales";
        gallery.Items.Select(i => i.Entry.Id).ShouldBe(["crm", "shop"]);

        gallery.Search = "orders";
        gallery.Items.Select(i => i.Entry.Id).ShouldBe(["shop"]);

        gallery.Search = "nothing like this";
        gallery.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Picking_a_template_fills_the_wizard()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.ShowGalleryCommand.Execute(null);

        main.Gallery.Pick(main.Gallery.Items.Single(i => i.Entry.Id == "helpdesk"));

        main.HasPage.ShouldBeFalse();
        main.Current.ShouldBe(main.Basics);
        main.Basics.Name.ShouldBe("Helpdesk");
        main.Option("files").IsOn.ShouldBeTrue();
        main.Errors.ShouldBeEmpty();
        main.Review.IsReachable.ShouldBeTrue();
        main.ToSpec().Modules.ShouldHaveSingleItem().Entities.Select(e => e.Name).ShouldBe(["Ticket", "TicketComment"]);
    }

    [Fact]
    public Task The_welcome_page_opens_the_gallery() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        var window = new MainWindow { DataContext = main };
        window.Show();

        main.Welcome.Main.ShowGalleryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<GalleryView>().ShouldHaveSingleItem();
        window.Close();
    });
}
