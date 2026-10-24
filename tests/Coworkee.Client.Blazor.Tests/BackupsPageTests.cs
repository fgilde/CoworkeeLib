using Bunit;
using Coworkee.Client.Blazor.Backup;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Backup;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class BackupsPageTests : ClientTestBase
{
    private readonly IBackupApi _api = Substitute.For<IBackupApi>();

    public BackupsPageTests()
    {
        Services.AddSingleton(_api);
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(BackupPermissions.Manage));
    }

    [Fact]
    public async Task Lists_backups_and_restores_one_after_confirmation()
    {
        var backup = new BackupDto(Guid.CreateVersion7(), "backup-20261008-120000.zip", 2048, 12, "20261008_Init", DateTimeOffset.UtcNow);
        _api.GetAsync(default).ReturnsForAnyArgs([backup]);
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var page = Render<Backups>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("backup-20261008-120000.zip"));
        page.Markup.ShouldContain("2 KB");

        var restoring = page.Find("[data-testid='restore-backup']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Restore").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Restore").ClickAsync(new());
        await restoring;

        await _api.Received(1).RestoreAsync(backup.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_uploaded_zip_goes_to_the_api_and_the_list_reloads()
    {
        _api.GetAsync(default).ReturnsForAnyArgs([]);
        Render<MudPopoverProvider>();
        var page = Render<Backups>();
        var upload = page.FindComponent<MudExUploadEdit<UploadableFile>>();

        await upload.InvokeAsync(() => upload.Instance.UploadRequestDataLoaded.InvokeAsync(new UploadableFile { FileName = "old.zip", ContentType = "application/zip", Data = [1, 2] }));

        await _api.Received(1).UploadAsync("old.zip", Arg.Is<Stream>(s => s.Length == 2), Arg.Any<CancellationToken>());
        await _api.Received(2).GetAsync(Arg.Any<CancellationToken>());
    }
}
