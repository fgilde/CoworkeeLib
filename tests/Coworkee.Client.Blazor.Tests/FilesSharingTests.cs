using Bunit;
using Coworkee.Client.Blazor.Files;
using Coworkee.Contracts.Files;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Core.FileManager;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class FilesSharingTests : ClientTestBase
{
    private readonly IFilesApi _files = Substitute.For<IFilesApi>();
    private readonly StoredFileDto _photo = new(Guid.CreateVersion7(), null, "photo.png", "image/png", 2048, DateTimeOffset.UtcNow, null);
    private readonly StoredFileDto _shared = new(Guid.CreateVersion7(), null, "shared.pdf", "application/pdf", 4096, DateTimeOffset.UtcNow, null, IsPublic: true);

    public FilesSharingTests()
    {
        Services.AddSingleton(_files);
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(FilePermissions.View));
        _files.SetPublicAsync(_photo.Id, true, Arg.Any<CancellationToken>()).Returns(_photo with { IsPublic = true });
        _files.SetPublicAsync(_shared.Id, false, Arg.Any<CancellationToken>()).Returns(_shared with { IsPublic = false });
    }

    private IRenderedComponent<Pages.Files> RenderPage(bool canManage)
    {
        _files.GetContentAsync(null, Arg.Any<CancellationToken>()).Returns(new FolderContentDto([], [_photo, _shared], true, canManage));
        Render<MudPopoverProvider>();
        var page = Render<Pages.Files>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("photo.png"));
        return page;
    }

    private static Task SelectAsync(IRenderedComponent<Pages.Files> page, string name)
    {
        var manager = page.FindComponent<MudExFileManager>();
        var node = manager.Instance.Manager.GetRootAsync().Result.Single(n => n.Name == name);
        return page.InvokeAsync(() => manager.Instance.SelectedNodesChanged.InvokeAsync([node]));
    }

    [Fact]
    public void Public_files_carry_a_badge_in_the_file_area()
    {
        var page = RenderPage(canManage: false);

        page.WaitForAssertion(() => page.FindAll("[data-testid='file-public-icon']").Count.ShouldBe(1));
    }

    [Fact]
    public async Task A_manager_makes_a_file_public_after_confirming_and_copies_its_link()
    {
        var dialogs = Render<MudDialogProvider>();
        var page = RenderPage(canManage: true);
        await SelectAsync(page, "photo.png");
        page.Find("[data-testid='file-copy-link']").HasAttribute("disabled").ShouldBeTrue();

        var toggling = page.InvokeAsync(() => page.Find("[data-testid='file-toggle-public']").ClickAsync(new()));
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Make public").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Make public").ClickAsync(new());
        await toggling;

        await _files.Received(1).SetPublicAsync(_photo.Id, true, Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.Find("[data-testid='file-public-badge']"));
        page.Find("[data-testid='file-toggle-public']").TextContent.ShouldContain("Make private");
        await page.InvokeAsync(() => page.Find("[data-testid='file-copy-link']").ClickAsync(new()));
        JSInterop.Invocations["navigator.clipboard.writeText"].Single().Arguments[0].ShouldBe($"http://localhost/api/v1/files/public/{_photo.Id}/photo.png");
    }

    [Fact]
    public async Task Any_file_gets_a_temporary_link_that_stays_private()
    {
        var page = RenderPage(canManage: false);
        _files.CreateTokenAsync(_photo.Id, Arg.Any<CancellationToken>()).Returns(new FileTokenDto("t0k", FileLinks.Token("t0k", "photo.png"), DateTimeOffset.UtcNow.AddMinutes(5)));
        await SelectAsync(page, "photo.png");

        await page.InvokeAsync(() => page.Find("[data-testid='file-copy-temporary-link']").ClickAsync(new()));

        JSInterop.Invocations["navigator.clipboard.writeText"].Single().Arguments[0].ShouldBe("http://localhost/api/v1/files/token/t0k/photo.png");
        await _files.DidNotReceive().SetPublicAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Making_a_file_private_needs_no_question()
    {
        var page = RenderPage(canManage: true);
        await SelectAsync(page, "shared.pdf");

        await page.InvokeAsync(() => page.Find("[data-testid='file-toggle-public']").ClickAsync(new()));

        await _files.Received(1).SetPublicAsync(_shared.Id, false, Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.FindAll("[data-testid='file-public-badge']").ShouldBeEmpty());
    }

    [Fact]
    public async Task Without_the_manage_right_there_is_no_toggle_but_the_link_of_a_public_file()
    {
        var page = RenderPage(canManage: false);
        await SelectAsync(page, "shared.pdf");

        page.FindAll("[data-testid='file-toggle-public']").ShouldBeEmpty();
        page.Find("[data-testid='file-copy-link']").HasAttribute("disabled").ShouldBeFalse();
    }
}
