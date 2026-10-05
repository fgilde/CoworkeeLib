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

        page.WaitForElement("input[data-testid='profile-last'], [data-testid='profile-last'] input").Input("Lovelace");
        await page.Find("[data-testid='save-profile']").ClickAsync(new());

        await Api.Received(1).UpdateMyProfileAsync(Arg.Is<UpdateProfileRequest>(r => r.FirstName == "Ada" && r.LastName == "Lovelace"), Arg.Any<CancellationToken>());
        page.Markup.ShouldContain("Ada Lovelace");
        page.FindAll("a").Select(a => a.GetAttribute("href")).ShouldContain("https://auth.test/Account/Manage/ChangePassword");
    }
}
