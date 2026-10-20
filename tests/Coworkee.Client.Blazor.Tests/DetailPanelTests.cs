using Bunit;
using Bunit.TestDoubles;
using Coworkee.Client.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class DetailPanelTests : ClientTestBase
{
    private readonly Guid _id = Guid.CreateVersion7();

    private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

    private IRenderedComponent<CoworkeeDetailPanel<Guid>> Render(Action<Guid?>? changed = null) =>
        Render<CoworkeeDetailPanel<Guid>>(p => p
            .Add(d => d.Width, "600px")
            .Add(d => d.Title, id => b => b.AddContent(0, $"Card {id}"))
            .Add(d => d.Actions, id => b => b.AddMarkupContent(0, "<button data-testid='edit'>Edit</button>"))
            .Add(d => d.ChildContent, id => b => b.AddMarkupContent(0, $"<p data-testid='body'>{id}</p>"))
            .Add(d => d.KeyChanged, EventCallback.Factory.Create<Guid>(this, k => changed?.Invoke(k == Guid.Empty ? null : k))));

    [Fact]
    public void A_link_with_the_query_parameter_opens_the_item_and_the_back_button_closes_it()
    {
        Nav.NavigateTo("/board");
        var panel = Render();
        panel.FindAll("[data-testid='body']").ShouldBeEmpty();

        Nav.NavigateTo($"/board?item={_id}");

        panel.WaitForAssertion(() => panel.Find("[data-testid='body']").TextContent.ShouldBe(_id.ToString()));
        panel.Find("[data-testid='detail-panel-title']").TextContent.ShouldBe($"Card {_id}");
        panel.Find("[data-testid='edit']").ShouldNotBeNull();
        panel.Markup.ShouldContain("min(100vw, 600px)");

        Nav.NavigateTo("/board");
        panel.WaitForAssertion(() => panel.FindAll("[data-testid='body']").ShouldBeEmpty());
        panel.Instance.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Open_adds_a_history_entry_and_escape_or_the_close_button_remove_the_parameter()
    {
        Guid? last = null;
        Nav.NavigateTo("/board?view=list");
        var panel = Render(k => last = k);

        await panel.InvokeAsync(() => panel.Instance.Open(_id));

        panel.WaitForAssertion(() => panel.Find("[data-testid='body']"));
        last.ShouldBe(_id);
        Nav.Uri.ShouldEndWith($"/board?view=list&item={_id}");
        Nav.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();

        await panel.Find("[role='dialog']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        panel.WaitForAssertion(() => panel.FindAll("[data-testid='body']").ShouldBeEmpty());
        Nav.Uri.ShouldEndWith("/board?view=list");
        Nav.History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
        last.ShouldBeNull();

        Nav.NavigateTo($"/board?item={_id}");
        panel.WaitForAssertion(() => panel.Find("[data-testid='detail-panel-close']"));
        await panel.Find("[data-testid='detail-panel-close']").ClickAsync(new());
        panel.WaitForAssertion(() => panel.FindAll("[data-testid='body']").ShouldBeEmpty());
        Nav.Uri.ShouldEndWith("/board");
    }

    [Fact]
    public void A_parameter_that_is_no_key_leaves_the_panel_closed()
    {
        Nav.NavigateTo("/board?item=nonsense");
        var panel = Render();
        panel.Instance.IsOpen.ShouldBeFalse();
        panel.FindAll("[data-testid='body']").ShouldBeEmpty();
    }
}
