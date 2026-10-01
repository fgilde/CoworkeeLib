using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts;
using Coworkee.Contracts.Auditing;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class AuditComponentsTests : ClientTestBase
{
    private readonly Guid _id = Guid.CreateVersion7();

    [Fact]
    public void Timeline_shows_changes_with_old_and_new_values()
    {
        Api.GetAuditAsync(Arg.Is<AuditQuery>(q => q.EntityType == "Theme" && q.EntityId == _id.ToString()), Arg.Any<CancellationToken>()).Returns(
            new PagedResult<AuditEntryDto>(
                [new AuditEntryDto(Guid.CreateVersion7(), "Theme", _id.ToString(), "Updated", Guid.CreateVersion7(), "Ada Admin", DateTimeOffset.UtcNow, null,
                    [new AuditChangeDto("Name", "\"Old\"", "\"New\""), new AuditChangeDto("Company", null, "\"M\u00FCller \u0026 S\u00F6hne\"")])],
                1, 1, 25));

        var timeline = Render<AuditTimeline>(p => p.Add(t => t.EntityType, "Theme").Add(t => t.EntityId, _id.ToString()));

        timeline.WaitForAssertion(() => timeline.Markup.ShouldContain("Ada Admin"));
        timeline.Find("[data-change='Company']").TextContent.ShouldContain("Müller & Söhne");
        timeline.Find("[data-change='Name']").TextContent.ShouldSatisfyAllConditions(t => t.ShouldContain("Old"), t => t.ShouldContain("New"));
    }

    [Fact]
    public async Task Restore_calls_the_api_and_raises_the_event()
    {
        Api.GetVersionsAsync("Theme", _id, Arg.Any<CancellationToken>()).Returns(
        [
            new EntityVersionDto(2, DateTimeOffset.UtcNow, null, "Ada Admin", false),
            new EntityVersionDto(1, DateTimeOffset.UtcNow.AddMinutes(-5), null, "Ada Admin", false),
        ]);
        var restored = 0;
        var history = Render<VersionHistory>(p => p.Add(h => h.Type, "Theme").Add(h => h.Id, _id).Add(h => h.OnRestored, () => restored++));

        await history.WaitForElement("[data-testid='restore-1']").ClickAsync(new());

        await Api.Received(1).RestoreVersionAsync("Theme", _id, 1, Arg.Any<CancellationToken>());
        restored.ShouldBe(1);
        history.FindAll("[data-testid='restore-2']").ShouldBeEmpty();
    }
}
