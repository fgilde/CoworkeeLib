using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Contracts.Identity;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ProfileTests : ClientTestBase
{
    [Fact]
    public async Task Edits_the_own_profile_and_links_to_the_sign_in_pages()
    {
        Api.GetMyProfileAsync(Arg.Any<CancellationToken>()).Returns(new ProfileDto("ada@acme.test", "Ada", null, null));
        Api.UpdateMyProfileAsync(Arg.Any<UpdateProfileRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => new ProfileDto("ada@acme.test", c.Arg<UpdateProfileRequest>().FirstName, c.Arg<UpdateProfileRequest>().LastName, c.Arg<UpdateProfileRequest>().PhoneNumber));
        var auth = AddAuthorization();
        auth.SetAuthorized("Ada");
        auth.SetClaims(new Claim("manage_url", "https://auth.test/Account/Manage"));
        var page = Render<Profile>();

        page.WaitForAssertion(() => Fields(page).Count.ShouldBe(7));
        page.FindAll("form label").Select(l => l.TextContent.Trim()).ShouldContain("Last name");
        page.FindAll("form label").Select(l => l.TextContent.Trim()).ShouldContain("City");
        Fields(page)[1].Change("Lovelace");
        Fields(page)[5].Change("London");
        await page.Find("form").SubmitAsync();

        await Api.Received(1).UpdateMyProfileAsync(
            Arg.Is<UpdateProfileRequest>(r => r.FirstName == "Ada" && r.LastName == "Lovelace" && r.Address!.City == "London"), Arg.Any<CancellationToken>());
        page.Markup.ShouldContain("Ada Lovelace");

        Render<ProfileSecurity>().FindAll("a").Select(a => a.GetAttribute("href")).ShouldContain("https://auth.test/Account/Manage/ChangePassword");
    }

    [Fact]
    public void The_route_opens_the_security_tab()
    {
        Api.GetMyProfileAsync(Arg.Any<CancellationToken>()).Returns(new ProfileDto("ada@acme.test", "Ada", null, null));
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("manage_url", "https://auth.test/Account/Manage"));

        var page = Render<Profile>(p => p.Add(x => x.Tab, "security"));

        page.WaitForAssertion(() => page.Find("[data-testid='profile-security']"));
    }

    [Fact]
    public async Task Deleting_the_account_waits_for_the_own_address_and_signs_out()
    {
        Api.GetMyProfileAsync(Arg.Any<CancellationToken>()).Returns(new ProfileDto("ada@acme.test", "Ada", null, null));
        Api.LogoutAsync(Arg.Any<CancellationToken>()).Returns(new BffLogoutDto("/signed-out"));
        var page = Render<ProfilePrivacy>();
        var delete = () => page.Find("[data-testid='delete-account']");

        page.WaitForAssertion(() => delete().HasAttribute("disabled").ShouldBeTrue());
        page.Find("input[type='email']").Input("ADA@acme.test");
        await delete().ClickAsync(new());

        await Api.Received(1).DeleteMyAccountAsync("ADA@acme.test", Arg.Any<CancellationToken>());
        await Api.Received(1).LogoutAsync(Arg.Any<CancellationToken>());
    }

    // first name, last name, phone, street, zip code, city, country; the object edit's own filter box is left out
    private static List<AngleSharp.Dom.IElement> Fields(IRenderedComponent<Profile> page) =>
        [.. page.FindAll("form input[type='text'], form input[type='tel']").Where(i => i.GetAttribute("placeholder") != "Filter")];
}
