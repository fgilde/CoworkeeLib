using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.History;
using Coworkee.Contracts.Auditing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class EntityHistoryTests : ClientTestBase
{
    private readonly IEntityHistoryApi _history = Substitute.For<IEntityHistoryApi>();
    private readonly Guid _id = Guid.CreateVersion7();

    public EntityHistoryTests() => Services.AddSingleton(_history);

    [Fact]
    public void Changes_show_labels_readable_values_a_diff_for_long_text_and_restore_actions()
    {
        var longOld = string.Join(' ', Enumerable.Repeat("word", 30));
        _history.GetAsync("Products", _id, Arg.Any<CancellationToken>()).Returns(new EntityHistoryDto(true, false,
        [
            new EntityHistoryEntryDto(Guid.CreateVersion7(), "Updated", null, "Ada Admin", DateTimeOffset.UtcNow,
            [
                new EntityHistoryChangeDto("BrandId", "\"a\"", "\"b\"", "Acme", "Globex", true),
                new EntityHistoryChangeDto("Description", null, null, longOld, longOld + " more", true),
            ]),
            new EntityHistoryEntryDto(Guid.CreateVersion7(), "Created", null, "Ada Admin", DateTimeOffset.UtcNow.AddDays(-1),
                [new EntityHistoryChangeDto("Name", null, "\"Box\"", null, "Box", false)]),
        ]));

        var history = Render<CoworkeeEntityHistory>(p => p.Add(h => h.EntitySet, "Products").Add(h => h.Id, _id));

        history.WaitForAssertion(() => history.Find("[data-change='BrandId']").TextContent.ShouldContain("Brand"));
        history.Find("[data-change='BrandId']").TextContent.ShouldSatisfyAllConditions(t => t.ShouldContain("Acme"), t => t.ShouldContain("Globex"));
        history.Find("[data-testid='history-diff'] ins").TextContent.ShouldContain("more");
        history.FindAll("[data-testid='undo-change']").Count.ShouldBe(2);
        history.FindAll("[data-testid='restore-point']").Count.ShouldBe(1);
    }

    [Fact]
    public void Word_diff_marks_only_what_changed()
    {
        var parts = TextDiff.Compute("the quick brown fox", "the slow brown fox")!;

        string.Concat(parts.Where(p => p.Kind != DiffKind.Added).Select(p => p.Text)).ShouldBe("the quick brown fox");
        string.Concat(parts.Where(p => p.Kind != DiffKind.Removed).Select(p => p.Text)).ShouldBe("the slow brown fox");
        parts.Single(p => p.Kind == DiffKind.Removed).Text.ShouldBe("quick ");
    }
}
