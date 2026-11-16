using Bunit;
using Coworkee.Client.Blazor.Components.Files;
using Coworkee.Client.Blazor.Files;
using Coworkee.Contracts.Files;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Core.FileManager;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class FilesTests : ClientTestBase
{
    private readonly IFilesApi _files = Substitute.For<IFilesApi>();
    private readonly StoredFileDto _logo = new(Guid.CreateVersion7(), null, "logo.png", "image/png", 2048, DateTimeOffset.UtcNow, null);
    private readonly StoredFileDto _report = new(Guid.CreateVersion7(), null, "report.pdf", "application/pdf", 4096, DateTimeOffset.UtcNow, null);
    private readonly FolderDto _docs = new(Guid.CreateVersion7(), null, "Docs", DateTimeOffset.UtcNow, null);

    public FilesTests()
    {
        Services.AddSingleton(_files);
        _files.GetContentAsync(null, Arg.Any<CancellationToken>()).Returns(new FolderContentDto([_docs], [_logo, _report], true, false));
        _files.GetFileAsync(_logo.Id, Arg.Any<CancellationToken>()).Returns(_logo);
    }

    [Fact]
    public async Task Picker_shows_the_chosen_file_and_clears_it()
    {
        Guid? changed = _logo.Id;
        var picker = Render<CoworkeeFilePicker>(p => p.Add(x => x.Value, _logo.Id).Add(x => x.ValueChanged, id => changed = id));

        picker.WaitForAssertion(() => picker.Find($"[data-testid='file-chip-{_logo.Id}']").TextContent.ShouldContain("logo.png"));
        await picker.Find($"[data-testid='file-chip-{_logo.Id}'] button").ClickAsync(new());

        changed.ShouldBeNull();
    }

    [Fact]
    public void Picker_offers_only_accepted_files_in_a_side_sheet()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var picker = Render<CoworkeeFilePicker>(p => p.Add(x => x.Accept, "image/*"));

        _ = picker.Find("[data-testid='file-picker-open']").ClickAsync(new());

        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("logo.png"));
        dialogs.Markup.ShouldContain("Docs");
        dialogs.Markup.ShouldNotContain("report.pdf");
        dialogs.Find(".mud-dialog").ClassName!.ShouldContain(MudBlazor.Extensions.Helper.MudExCss.Classes.Dialog.Initial.ToString());
    }

    [Fact]
    public async Task Opening_a_file_in_the_side_sheet_chooses_it()
    {
        Guid? chosen = null;
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var picker = Render<CoworkeeFilePicker>(p => p.Add(x => x.ValueChanged, id => chosen = id));
        var choosing = picker.Find("[data-testid='file-picker-open']").ClickAsync(new());

        await dialogs.WaitForElement($"[data-file-grid-key='{_logo.Id}']").DoubleClickAsync(new());
        await choosing;

        chosen.ShouldBe(_logo.Id);
        picker.WaitForAssertion(() => picker.Markup.ShouldContain("logo.png"));
    }

    [Fact]
    public async Task Preview_loads_the_file_from_an_absolute_url()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();

        await dialogs.InvokeAsync(() => FilePreviewDialog.ShowAsync(Services.GetRequiredService<IDialogService>(), _report));

        dialogs.WaitForAssertion(() => dialogs.FindComponent<MudExFileDisplay>().Instance.Url.ShouldBe($"http://localhost/api/v1/files/{_report.Id}/content"));
    }

    [Fact]
    public async Task Preview_shows_any_content_url_of_the_app()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();

        await dialogs.InvokeAsync(() => FilePreviewDialog.ShowAsync(Services.GetRequiredService<IDialogService>(), "api/v1/documents/42/content", "application/pdf", "offer.pdf"));

        dialogs.WaitForAssertion(() =>
        {
            var display = dialogs.FindComponent<MudExFileDisplay>().Instance;
            display.Url.ShouldBe("http://localhost/api/v1/documents/42/content");
            display.ContentType.ShouldBe("application/pdf");
            display.FileName.ShouldBe("offer.pdf");
        });
        dialogs.Markup.ShouldContain("offer.pdf");
    }

    [Fact]
    public void Files_page_lists_the_root_with_the_rights_of_the_folder()
    {
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(FilePermissions.View));

        var page = Render<Pages.Files>();

        page.WaitForAssertion(() => page.Markup.ShouldContain("report.pdf"));
        var manager = page.FindComponent<MudExFileManager>().Instance.Manager;
        manager.Capabilities.HasFlag(MudExFileManagerCapabilities.Upload).ShouldBeTrue();
        manager.Capabilities.HasFlag(MudExFileManagerCapabilities.Delete).ShouldBeFalse();
    }

    [Fact]
    public async Task Files_page_follows_changes_of_other_sessions_and_stays_in_the_folder()
    {
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(FilePermissions.View));
        _files.GetContentAsync(_docs.Id, Arg.Any<CancellationToken>()).Returns(new FolderContentDto([], [], true, false));
        var page = Render<Pages.Files>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("report.pdf"));
        var drive = page.FindComponent<MudExFileManager>();
        await page.InvokeAsync(() => drive.Instance.NavigateToAsync(drive.Instance.RootItems.Single(n => n.Name == "Docs")));

        var added = _report with { Id = Guid.CreateVersion7(), FolderId = _docs.Id, Name = "added-elsewhere.pdf" };
        _files.GetContentAsync(_docs.Id, Arg.Any<CancellationToken>()).Returns(new FolderContentDto([], [added], true, false));
        var realtime = Services.GetRequiredService<FakeRealtimeConnection>();
        realtime.Invocations.ShouldContain(("Subscribe", "type:StoredFile"));
        realtime.Invocations.ShouldContain(("Subscribe", "type:FileFolder"));
        await page.InvokeAsync(() => realtime.Push("type:StoredFile"));

        page.WaitForAssertion(() => page.Markup.ShouldContain("added-elsewhere.pdf"));
        drive.Instance.CurrentDirectory!.Name.ShouldBe("Docs");
        ((FilesStructureManager)drive.Instance.Manager).CurrentFolderId.ShouldBe(_docs.Id);
    }

    [Fact]
    public async Task A_refused_upload_shows_the_rule_not_only_the_code()
    {
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(FilePermissions.View));
        const string reason = "Could not complete the security scan on the uploaded file.";
        _files.UploadAsync(null, "clip.mp4", "video/mp4", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns<StoredFileDto>(_ => throw new Api.ApiException(400, "validation", new Dictionary<string, string[]> { ["content"] = [reason] }, reason));
        var clip = Substitute.For<Microsoft.AspNetCore.Components.Forms.IBrowserFile>();
        clip.Name.Returns("clip.mp4");
        clip.ContentType.Returns("video/mp4");
        clip.Size.Returns(12L * 1024 * 1024);
        clip.OpenReadStream(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(new byte[16]));
        var page = Render<Pages.Files>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("report.pdf"));
        var drive = page.FindComponent<MudExFileManager>();

        await page.InvokeAsync(() => drive.Instance.UploadFilesAsync(null!, [clip]));

        page.WaitForAssertion(() => page.Markup.ShouldContain(reason));
        page.Markup.ShouldNotContain("ApiException: validation");
    }

    [Theory]
    [InlineData("image/*", "a.png", "image/png", true)]
    [InlineData("image/*", "a.pdf", "application/pdf", false)]
    [InlineData(".pdf, text/plain", "A.PDF", "application/octet-stream", true)]
    [InlineData("text/plain", "a.txt", "text/plain", true)]
    [InlineData(null, "a.bin", "application/octet-stream", true)]
    public void Accept_lists_match_like_the_browser(string? accept, string name, string contentType, bool expected) =>
        FileAccept.Matches(accept, name, contentType).ShouldBe(expected);
}
