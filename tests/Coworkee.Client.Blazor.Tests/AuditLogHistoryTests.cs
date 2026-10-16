using Bunit;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.History;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Auditing;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class AuditLogHistoryTests : ClientTestBase
{
    private static readonly Guid AdaId = Guid.CreateVersion7();
    private static readonly string[] Actions = ["audit-open-history", "audit-undo", "audit-restore-before", "audit-restore-record"];
    private readonly FakeODataClient _odata = new();
    private readonly IEntityHistoryApi _history = Substitute.For<IEntityHistoryApi>();
    private readonly Guid _record = Guid.CreateVersion7();

    public AuditLogHistoryTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        Services.AddSingleton(_history);
        AddAuthorization().SetAuthorized("Ada").SetClaims(new System.Security.Claims.Claim("sub", AdaId.ToString()))
            .SetPolicies(Security.PermissionPolicy.For(AuditPermissions.View));
        Render<MudPopoverProvider>();
    }

    [Theory]
    [InlineData("Updated", true, true, false, "audit-open-history,audit-undo,audit-restore-before")]
    [InlineData("Created", true, true, false, "audit-open-history")]
    [InlineData("Deleted", true, true, true, "audit-open-history,audit-restore-record")]
    [InlineData("Deleted", true, true, false, "")]
    [InlineData("Updated", true, false, false, "audit-open-history")]
    [InlineData("Updated", false, true, false, "")]
    public void Entries_offer_the_history_actions_their_set_and_permission_allow(string action, bool registered, bool canRestore, bool softDelete, string expected)
    {
        var page = Expanded(Entry(action), registered ? [new EntityHistorySetDto("Memo", "Memos", canRestore, softDelete)] : []);

        Actions.Where(a => page.FindAll($"[data-testid='{a}']").Count > 0).ShouldBe(expected.Split(',', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Undo_previews_the_change_with_later_changes_marked_and_restores_after_confirmation()
    {
        var entry = Entry("Updated");
        var request = new EntityHistoryRestoreRequest(entry.Id, Mode: EntityHistoryRestoreMode.Undo);
        _history.PreviewAsync("Memos", _record, request, Arg.Any<CancellationToken>())
            .Returns(new EntityHistoryPreviewDto(false, [new EntityHistoryFieldDto("Text", "three", "one", true)]));
        var dialogs = Render<MudDialogProvider>();
        var page = Expanded(entry, [new EntityHistorySetDto("Memo", "Memos", true, false)]);

        var undoing = page.Find("[data-testid='audit-undo']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='restore-changed-later']").ShouldNotBeNull());
        dialogs.Find("[data-field='Text']").TextContent.ShouldSatisfyAllConditions(t => t.ShouldContain("three"), t => t.ShouldContain("one"));
        await dialogs.Find("[data-testid='confirm-restore']").ClickAsync(new());
        await undoing;

        await _history.Received(1).RestoreAsync("Memos", _record, request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Open_record_history_shows_the_timeline_of_the_record()
    {
        _history.GetAsync("Memos", _record, Arg.Any<CancellationToken>()).Returns(new EntityHistoryDto(false, false,
            [new EntityHistoryEntryDto(Guid.CreateVersion7(), "Updated", null, "Ada Admin", DateTimeOffset.UtcNow, [new EntityHistoryChangeDto("Text", null, null, "one", "two", false)])]));
        var dialogs = Render<MudDialogProvider>();
        var page = Expanded(Entry("Updated"), [new EntityHistorySetDto("Memo", "Memos", false, false)]);

        await page.Find("[data-testid='audit-open-history']").ClickAsync(new());

        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='entity-history']").TextContent.ShouldContain("Ada Admin"));
    }

    private AuditEntryDto Entry(string action) =>
        new(Guid.CreateVersion7(), "Memo", _record.ToString(), action, AdaId, null, DateTimeOffset.UtcNow, null, [new AuditChangeDto("Text", "one", "two")]);

    private IRenderedComponent<AuditLog> Expanded(AuditEntryDto entry, EntityHistorySetDto[] sets)
    {
        _history.GetSetsAsync(Arg.Any<CancellationToken>()).Returns(sets);
        _odata.With("AuditEntries", entry);
        var page = Render<AuditLog>();
        page.WaitForAssertion(() => page.Find(".mud-table-body .mud-table-row button").Click());
        page.WaitForAssertion(() => page.Find("[data-change='Text']").ShouldNotBeNull());
        return page;
    }
}
