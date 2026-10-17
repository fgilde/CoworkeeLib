using Coworkee.Client.Blazor.Navigation;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CoworkeePagesTests
{
    private static readonly Guid Id = Guid.Parse("6f1b7c1e-3a59-4c43-9d55-0d7c3f0c2a11");

    private static CoworkeePages Pages(Action<CoworkeeClientOptions>? configure = null)
    {
        var options = new CoworkeeClientOptions();
        configure?.Invoke(options);
        return new CoworkeePages(LibraryPages.All, options);
    }

    [Fact]
    public void Admin_pages_move_under_the_prefix_and_other_pages_stay()
    {
        var pages = Pages();

        pages.Href($"/admin/users/{Id}?tab=roles").ShouldBe($"/cw/admin/users/{Id}?tab=roles");
        pages.Href("/admin/mail/templates/Welcome%20mail/de").ShouldBe("/cw/admin/mail/templates/Welcome%20mail/de");
        pages.Href("/chat?with=1").ShouldBe("/chat?with=1");
        pages.Href("/catalog/products").ShouldBe("/catalog/products");
    }

    [Fact]
    public void An_empty_prefix_keeps_the_old_addresses()
    {
        var pages = Pages(o => o.PagePrefix = "");

        pages.Href("/admin/users").ShouldBe("/admin/users");
        pages.Match("admin/users")!.Component.ShouldBe(typeof(Pages.Admin.Users));
    }

    [Fact]
    public void Old_addresses_go_on_to_the_current_ones()
    {
        var match = Pages().Match($"admin/users/{Id}?x=1")!;

        match.RedirectTo.ShouldBe($"/cw/admin/users/{Id}?x=1");
        Pages().Match($"cw/admin/users/{Id}")!.Values["Id"].ShouldBe(Id);
        Pages().Match("admin/users/not-a-guid").ShouldBeNull();
    }

    [Fact]
    public void A_replaced_page_renders_the_apps_component_at_the_librarys_address()
    {
        var pages = Pages(o => o.Pages.Replace<AppUsersPage>("users"));

        pages.Match("cw/admin/users")!.Component.ShouldBe(typeof(AppUsersPage));
    }

    [Fact]
    public void A_page_replaced_by_an_own_route_sends_links_and_old_addresses_there()
    {
        var pages = Pages(o => o.Pages.Replace("user", "/team/{Id}"));

        pages.Href($"/admin/users/{Id}").ShouldBe($"/team/{Id}");
        pages.Match($"cw/admin/users/{Id}")!.RedirectTo.ShouldBe($"/team/{Id}");
        pages.Match($"team/{Id}").ShouldBeNull();
    }

    [Fact]
    public void A_turned_off_page_has_no_link_and_no_address()
    {
        var pages = Pages(o => o.Pages.Disable("backups"));

        pages.Href("/admin/backups").ShouldBeNull();
        pages.Match("cw/admin/backups").ShouldBeNull();
        pages.Match("admin/backups").ShouldBeNull();
    }

    [Fact]
    public void An_unknown_key_fails_at_start()
    {
        Should.Throw<InvalidOperationException>(() => Pages(o => o.Pages.Disable("nope")));
    }
}
