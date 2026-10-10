using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Files;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Options;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CloudFilePickerTests : ClientTestBase
{
    [Fact]
    public void Upload_fields_offer_the_pickers_that_have_an_id()
    {
        CloudFilePickers.Apply(Services.GetRequiredService<MudExConfiguration>(), new Dictionary<string, string?>
        {
            [FilePickerSettings.GoogleDriveEnabled] = "true",
            [FilePickerSettings.GoogleDriveClientId] = " google-client ",
            [FilePickerSettings.OneDriveClientId] = "",
        });

        var upload = Render<MudExUploadEdit<UploadableFile>>();

        upload.WaitForAssertion(() => upload.FindComponent<MudExGoogleFilePicker>().Instance.ClientId.ShouldBe("google-client"));
        upload.FindComponents<MudExOneDriveFilePicker>().ShouldBeEmpty();
        upload.FindComponents<MudExDropBoxFilePicker>().ShouldBeEmpty();
    }

    [Fact]
    public void A_disabled_picker_stays_hidden_with_its_id()
    {
        CloudFilePickers.Apply(Services.GetRequiredService<MudExConfiguration>(), new Dictionary<string, string?>
        {
            [FilePickerSettings.DropboxEnabled] = "False",
            [FilePickerSettings.DropboxAppKey] = "dropbox-key",
        });

        var upload = Render<MudExUploadEdit<UploadableFile>>();

        upload.FindComponents<MudExDropBoxFilePicker>().ShouldBeEmpty();
    }

    [Fact]
    public void The_layout_applies_the_client_settings()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
        Api.GetClientSettingsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string?>
        {
            [FilePickerSettings.OneDriveEnabled] = "true",
            [FilePickerSettings.OneDriveClientId] = "onedrive-client",
        });
        AddAuthorization().SetAuthorized("Ada");
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));
        layout.WaitForAssertion(() => Api.Received().GetClientSettingsAsync(Arg.Any<CancellationToken>()));

        var upload = Render<MudExUploadEdit<UploadableFile>>();

        upload.WaitForAssertion(() => upload.FindComponent<MudExOneDriveFilePicker>().Instance.ClientId.ShouldBe("onedrive-client"));
    }
}
