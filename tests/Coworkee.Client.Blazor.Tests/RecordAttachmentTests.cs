using Bunit;
using Coworkee.Client.Blazor.Attachments;
using Coworkee.Client.Blazor.Components.Attachments;
using Coworkee.Contracts.Attachments;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class RecordAttachmentTests : ClientTestBase
{
    private readonly IAttachmentsApi _api = Substitute.For<IAttachmentsApi>();
    private readonly Guid _id = Guid.CreateVersion7();

    public RecordAttachmentTests()
    {
        Services.AddSingleton(_api);
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
    }

    private RecordAttachmentDto File(string name) => new(Guid.CreateVersion7(), "LeaveRequests", _id, "certificate", name, "application/pdf", 2048, DateTimeOffset.UtcNow, null);

    private static AttachmentSlotDto Slot(bool canWrite, params RecordAttachmentDto[] items) => new("certificate", 1024 * 1024, ["application/pdf"], false, canWrite, items);

    private IRenderedComponent<RecordAttachment> Render() =>
        Render<RecordAttachment>(p => p.Add(a => a.EntityType, "LeaveRequests").Add(a => a.EntityId, _id).Add(a => a.Slot, "certificate"));

    [Fact]
    public void A_picked_file_is_uploaded_to_the_slot_and_the_list_reloads()
    {
        var uploaded = File("note.pdf");
        _api.GetSlotAsync("LeaveRequests", _id, "certificate", Arg.Any<CancellationToken>()).Returns(Slot(true), Slot(true, uploaded));
        var attachment = Render();
        attachment.WaitForAssertion(() => attachment.FindComponent<InputFile>());

        attachment.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "note.pdf", null, "application/pdf"));

        attachment.WaitForAssertion(() => _api.Received(1).UploadAsync("LeaveRequests", _id, "certificate", "note.pdf", "application/pdf", Arg.Any<Stream>(), Arg.Any<CancellationToken>()));
        attachment.WaitForAssertion(() => attachment.Find($"[data-attachment='{uploaded.Id}']").TextContent.ShouldContain("note.pdf"));
        attachment.Find($"[data-attachment='{uploaded.Id}'] a[href]").GetAttribute("href").ShouldBe(AttachmentUrls.Content(uploaded, download: true));
    }

    [Fact]
    public void A_change_on_the_record_topic_reloads_the_slot()
    {
        var added = File("by-someone-else.pdf");
        _api.GetSlotAsync("LeaveRequests", _id, "certificate", Arg.Any<CancellationToken>()).Returns(Slot(false), Slot(false, added));
        var attachment = Render();
        var realtime = Services.GetRequiredService<FakeRealtimeConnection>();
        attachment.WaitForAssertion(() => realtime.Invocations.ShouldContain(("Subscribe", AttachmentTopics.Record("LeaveRequests", _id))));

        realtime.Push(AttachmentTopics.Record("LeaveRequests", _id));

        attachment.WaitForAssertion(() => attachment.Find($"[data-attachment='{added.Id}']").TextContent.ShouldContain("by-someone-else.pdf"));
    }

    [Fact]
    public async Task Delete_asks_first_then_removes_the_file()
    {
        var file = File("old.pdf");
        _api.GetSlotAsync("LeaveRequests", _id, "certificate", Arg.Any<CancellationToken>()).Returns(Slot(true, file), Slot(true));
        var dialogs = Render<MudDialogProvider>();
        var attachment = Render();
        attachment.WaitForAssertion(() => attachment.Find("[data-testid='attachment-delete']"));

        var deleting = attachment.Find("[data-testid='attachment-delete']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("old.pdf"));
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Delete").ClickAsync(new());
        await deleting;

        await _api.Received(1).DeleteAsync(file, Arg.Any<CancellationToken>());
        attachment.WaitForAssertion(() => attachment.FindAll("[data-attachment]").ShouldBeEmpty());
    }

    [Fact]
    public void Without_write_access_or_read_only_there_is_no_upload_and_no_delete()
    {
        var file = File("cv.pdf");
        _api.GetSlotAsync("LeaveRequests", _id, "certificate", Arg.Any<CancellationToken>()).Returns(Slot(false, file));
        var attachment = Render();
        attachment.WaitForAssertion(() => attachment.Find($"[data-attachment='{file.Id}']"));
        attachment.FindComponents<InputFile>().ShouldBeEmpty();
        attachment.FindAll("[data-testid='attachment-delete']").ShouldBeEmpty();

        _api.GetSlotAsync("LeaveRequests", _id, "certificate", Arg.Any<CancellationToken>()).Returns(Slot(true));
        var readOnly = Render<RecordAttachment>(p => p.Add(a => a.EntityType, "LeaveRequests").Add(a => a.EntityId, _id).Add(a => a.Slot, "certificate").Add(a => a.ReadOnly, true));
        readOnly.WaitForAssertion(() => readOnly.Markup.ShouldContain("None"));
        readOnly.FindComponents<InputFile>().ShouldBeEmpty();
    }
}
