using Bunit;
using Coworkee.Client.Blazor.Components.Files;
using Coworkee.Client.Blazor.Components.Users;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>Tabs that modules and apps add to the admin user page (<see cref="UserDetailTabs.AddUserDetailTab{TComponent}"/>), and the library's own: the user's files.</summary>
public sealed class UserDetailExtraTabsTests : ClientTestBase
{
    private const string NotesPermission = "Notes.View";
    private readonly Guid _user = Guid.CreateVersion7();
    private readonly FakeODataClient _odata = new();
    private Bunit.TestDoubles.BunitAuthorizationContext? _auth;

    public UserDetailExtraTabsTests()
    {
        Services.AddSingleton<Data.IODataClient>(_odata);
        Services.AddUserDetailTab<UserIdProbe>("Notes", NotesPermission);
        Api.GetUserDetailAsync(_user, Arg.Any<CancellationToken>()).Returns(new UserDetailDto(
            _user, "bob@acme.test", "bob@acme.test", "Bob", null, true, true, false, null, null, [], []));
        Api.GetEffectivePermissionsAsync(_user, Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public async Task A_contributed_tab_shows_with_its_permission_and_gets_the_user_id()
    {
        SignIn(IdentityPermissions.Users.View, NotesPermission);

        var page = Render<UserDetail>(p => p.Add(d => d.Id, _user));

        page.WaitForAssertion(() => page.FindAll(".mud-tab").Select(t => t.TextContent.Trim()).ShouldContain("Notes"));
        await page.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == "Notes").ClickAsync(new());
        page.WaitForAssertion(() => page.Find("[data-testid='probe']").TextContent.ShouldBe(_user.ToString()));
    }

    [Fact]
    public void A_contributed_tab_hides_without_its_permission()
    {
        SignIn(IdentityPermissions.Users.View);

        var page = Render<UserDetail>(p => p.Add(d => d.Id, _user));

        page.WaitForElement("[data-testid='user-name']");
        page.FindAll(".mud-tab").Select(t => t.TextContent.Trim()).ShouldNotContain("Notes");
    }

    [Fact]
    public void The_files_tab_groups_registration_documents_apart_and_reloads_live()
    {
        SignIn(IdentityPermissions.Users.View, FilePermissions.View);
        var root = new FolderDto(Guid.CreateVersion7(), null, FolderDto.RegistrationsRoot, default, null);
        var bobs = new FolderDto(Guid.CreateVersion7(), root.Id, "bob", default, null);
        var shared = new FolderDto(Guid.CreateVersion7(), null, "Shared", default, null);
        var passport = File("Passport - scan.pdf", bobs.Id);
        var offer = File("Offer.docx", shared.Id);
        var loose = File("Notes.txt", null);
        _odata.With("StoredFiles", passport, offer, loose).With("FileFolders", root, bobs, shared);
        var queries = new List<Data.ODataQuery>();
        _odata.Queried += (_, q) => queries.Add(q);

        var tab = Render<UserFilesTab>(p => p.Add(t => t.UserId, _user));

        tab.WaitForAssertion(() => tab.FindAll("[data-testid='user-files-registration'] [data-file]").Select(r => r.GetAttribute("data-file")).ShouldBe([passport.Id.ToString()]));
        tab.FindAll("[data-testid='user-files-other'] [data-file]").Select(r => r.GetAttribute("data-file")).ShouldBe([offer.Id.ToString(), loose.Id.ToString()]);
        tab.Find($"[data-file='{offer.Id}'] a[href]").GetAttribute("href")!.ShouldBe($"api/v1/files/{offer.Id}/content?download=true");
        queries[0].Filter.ShouldBe($"CreatedBy eq {_user}");

        var before = queries.Count;
        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.Type("StoredFile"));
        tab.WaitForAssertion(() => queries.Count.ShouldBeGreaterThan(before));
    }

    [Fact]
    public void The_files_tab_says_when_there_are_none()
    {
        var tab = Render<UserFilesTab>(p => p.Add(t => t.UserId, _user));

        tab.WaitForElement("[data-testid='user-files-empty']");
    }

    [Fact]
    public void The_library_registers_the_files_tab_behind_the_files_permission()
    {
        var services = new ServiceCollection().AddCoworkeeClient(new Uri("https://app.test/"));

        var tab = services.Select(d => d.ImplementationInstance).OfType<UserDetailTab>().Single(t => t.Component == typeof(UserFilesTab));

        tab.Permission.ShouldBe(FilePermissions.View);
    }

    private StoredFileDto File(string name, Guid? folderId) => new(Guid.CreateVersion7(), folderId, name, "application/pdf", 2048, DateTimeOffset.UtcNow, _user);

    private void SignIn(params string[] permissions)
    {
        _auth ??= AddAuthorization().SetAuthorized("admin");
        _auth.SetPolicies([.. permissions.Select(Security.PermissionPolicy.For)]);
    }

    public sealed class UserIdProbe : ComponentBase
    {
        [Parameter] public Guid UserId { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "data-testid", "probe");
            builder.AddContent(2, UserId.ToString());
            builder.CloseElement();
        }
    }
}
