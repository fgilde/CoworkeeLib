using Bunit;
using Coworkee.Client.Blazor.Components.Chat;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ChatAttachDialogTests : ClientTestBase
{
    [Fact]
    public async Task The_listed_files_close_the_dialog_and_removed_ones_stay_behind()
    {
        var dialogs = Render<MudDialogProvider>();
        var picked = dialogs.InvokeAsync(() => ChatAttachDialog.ShowAsync(Services.GetRequiredService<IDialogService>(), "Attach files", 1024));
        var upload = dialogs.WaitForComponent<MudExUploadEdit<UploadableFile>>();
        var keep = new UploadableFile { FileName = "a.txt", ContentType = "text/plain", Data = [1] };
        var drop = new UploadableFile { FileName = "b.txt", ContentType = "text/plain", Data = [2] };

        await upload.InvokeAsync(() => upload.Instance.Add(keep));
        await upload.InvokeAsync(() => upload.Instance.Add(drop));
        await upload.InvokeAsync(() => upload.Instance.Remove(drop));
        await dialogs.Find("[data-testid='chat-attach-done']").ClickAsync(new());

        (await picked).ShouldBe([keep]);
    }
}
