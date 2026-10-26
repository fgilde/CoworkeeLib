using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Groups;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Gadget = Coworkee.Client.Blazor.Tests.DataTableLayoutTests.Gadget;

namespace Coworkee.Client.Blazor.Tests;

public sealed class GroupScopePickerTests : ClientTestBase
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly GroupNodeDto Customer = new(Guid.CreateVersion7(), null, "00001", "Customer A", null, 2, 5);
    private static readonly GroupNodeDto Hamburg = new(Guid.CreateVersion7(), Customer.Id, "00001.00001", "Hamburg", null, 0, 1);
    private static readonly GroupNodeDto Bremen = new(Guid.CreateVersion7(), Customer.Id, "00001.00002", "Bremen", null, 0, 0);
    private static readonly GroupNodeDto Supplier = new(Guid.CreateVersion7(), null, "00002", "Supplier", null, 0, 0);
    private readonly FakeODataClient _odata = new FakeODataClient().With("Gadgets", new Gadget(Guid.CreateVersion7(), "Drill", "Tools", 1));
    private readonly IClientDataCache _cache = Substitute.For<IClientDataCache>();
    private readonly HashSet<Guid> _selected = [];
    private List<GroupNodeDto> _mine = [Customer, Hamburg, Bremen, Supplier];
    private IRenderedComponent<MudPopoverProvider> _popovers = null!;
    private readonly List<string> _calls = [];
    private int _loads;

    public GroupScopePickerTests()
    {
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Me.ToString()));
        Services.AddSingleton<IODataClient>(_odata);
        Services.AddSingleton(_cache);
        Services.AddScoped<GroupScopeState>();
        _odata.Queried += (_, _) =>
        {
            _loads++;
            _calls.Add("query");
        };
        _cache.ClearAsync().Returns(_ =>
        {
            _calls.Add("clear");
            return Task.CompletedTask;
        });
        Api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        // the server: memberships Customer A and Supplier, the stored selection
        Api.GetMyGroupsAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _calls.Add("mine");
            return [.. _mine.Select(n => new MyGroupDto(n.Id, n.ParentId, n.Code, n.Name, n.ParentId is null, _selected.Contains(n.Id)))];
        });
        Api.SelectGroupsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _calls.Add("select");
            _selected.Clear();
            _selected.UnionWith(call.Arg<IReadOnlyList<Guid>>());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Hidden_without_groups()
    {
        _mine = [];

        var picker = RenderPicker();

        picker.WaitForAssertion(() => Api.Received().GetMyGroupsAsync(Arg.Any<CancellationToken>()));
        picker.WaitForAssertion(() => picker.FindAll("[data-testid='group-scope']").ShouldBeEmpty());
    }

    [Theory]
    [InlineData(0, "All my groups")]
    [InlineData(1, "Hamburg")]
    [InlineData(2, "2 groups")]
    public void Label_shows_all_one_or_count(int selected, string label)
    {
        _selected.UnionWith(new[] { Hamburg.Id, Supplier.Id }.Take(selected));

        var picker = RenderPicker();

        picker.WaitForAssertion(() => Button(picker).TextContent.Trim().ShouldBe(label));
    }

    [Fact]
    public async Task Selecting_sends_the_ids_and_reloads_tables()
    {
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => _loads.ShouldBe(1));
        var picker = RenderPicker();
        await OpenAsync(picker);
        _popovers.WaitForAssertion(() => _popovers.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A", "Bremen", "Hamburg", "Supplier"], ignoreOrder: false));
        _popovers.Find($"[data-group-node='{Customer.Id}']").GetAttribute("style")!.ShouldContain("font-weight", Case.Insensitive, "members are bold");

        await Check(Hamburg).ChangeAsync(new ChangeEventArgs { Value = true });

        picker.WaitForAssertion(() => Api.Received(1).SelectGroupsAsync(Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { Hamburg.Id })), Arg.Any<CancellationToken>()));
        picker.WaitForAssertion(() => Button(picker).TextContent.Trim().ShouldBe("Hamburg"));
        table.WaitForAssertion(() => _loads.ShouldBe(2));
        // the mirror is dropped before the tables ask again
        _calls.SkipWhile(c => c != "select").ShouldBe(["select", "mine", "clear", "query"]);
        await Api.DidNotReceive().GetGroupChildrenAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>());

        // the server echoes the selection to every tab of the user; this one has it already
        Realtime.Push($"user:{Me}", GroupSelectionEvents.Changed, new { groupIds = new[] { Hamburg.Id } });

        _calls.Count(c => c == "mine").ShouldBe(2);
        await _cache.Received(1).ClearAsync();
    }

    [Fact]
    public async Task A_rejected_selection_rolls_back()
    {
        Api.SelectGroupsAsync(Arg.Is<IReadOnlyList<Guid>>(ids => ids.Contains(Bremen.Id)), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(400, "validation", null));
        var picker = RenderPicker();
        await OpenAsync(picker);

        await Check(Bremen).ChangeAsync(new ChangeEventArgs { Value = true });

        picker.WaitForAssertion(() => Api.Received(1).SelectGroupsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()));
        _popovers.WaitForAssertion(() => Check(Bremen).HasAttribute("checked").ShouldBeFalse());
        Button(picker).TextContent.Trim().ShouldBe("All my groups");
        await _cache.DidNotReceive().ClearAsync();
        // nothing changed: another tab announcing "all groups" is this tab's state, not news
        Realtime.Push($"user:{Me}", GroupSelectionEvents.Changed, new { groupIds = Array.Empty<Guid>() });
        _calls.Count(c => c == "mine").ShouldBe(1);
    }

    [Fact]
    public async Task Without_client_entities_a_selection_still_raises_changed()
    {
        Services.RemoveAll<IClientDataCache>();
        var state = Services.GetRequiredService<GroupScopeState>();
        var changed = 0;
        state.Changed += () => changed++;

        await state.SelectAsync([Hamburg.Id]);

        changed.ShouldBe(1);
        state.Groups.Single(g => g.Selected).Id.ShouldBe(Hamburg.Id);
    }

    [Fact]
    public async Task A_failed_clear_still_raises_changed()
    {
        _cache.ClearAsync().ThrowsAsync(new InvalidOperationException("storage gone"));
        var state = Services.GetRequiredService<GroupScopeState>();
        var changed = 0;
        state.Changed += () => changed++;

        await state.SelectAsync([Hamburg.Id]);

        changed.ShouldBe(1);
    }

    [Fact]
    public async Task Loading_does_not_wait_for_the_realtime_hub_and_subscribes_once_it_is_there()
    {
        var hub = new TaskCompletionSource();
        Realtime.Gate = hub.Task;
        var state = Services.GetRequiredService<GroupScopeState>();

        await state.LoadAsync().WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        state.Groups.Count.ShouldBe(4);
        hub.SetException(new HttpRequestException("offline"));
        Realtime.Gate = Task.CompletedTask;
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);
        Realtime.Invocations.ShouldNotContain(("Subscribe", $"user:{Me}"));

        await state.LoadAsync().WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        Realtime.Invocations.ShouldContain(("Subscribe", $"user:{Me}"));
    }

    [Fact]
    public async Task Clearing_resets_to_all()
    {
        _selected.Add(Hamburg.Id);
        var picker = RenderPicker();
        await OpenAsync(picker);

        await _popovers.Find("[data-testid='group-scope-all']").ClickAsync(new());

        picker.WaitForAssertion(() => Api.Received(1).SelectGroupsAsync(Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 0), Arg.Any<CancellationToken>()));
        picker.WaitForAssertion(() => Button(picker).TextContent.Trim().ShouldBe("All my groups"));
    }

    [Fact]
    public void Realtime_selection_event_reloads_the_state()
    {
        var picker = RenderPicker();
        picker.WaitForAssertion(() => Button(picker).TextContent.Trim().ShouldBe("All my groups"));

        // another tab of the same user selected Supplier
        _selected.Add(Supplier.Id);
        Realtime.Push($"user:{Me}", GroupSelectionEvents.Changed, new { groupIds = new[] { Supplier.Id } });

        picker.WaitForAssertion(() => Button(picker).TextContent.Trim().ShouldBe("Supplier"));
        _cache.Received(1).ClearAsync();
    }

    [Fact]
    public void Scope_all_without_any_group_in_the_tenant_is_hidden()
    {
        _mine = [];
        Api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns([IdentityPermissions.Groups.ScopeAll]);
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns([]);

        var picker = RenderPicker();

        picker.WaitForAssertion(() => Api.Received(1).GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()));
        picker.WaitForAssertion(() => picker.FindAll("[data-testid='group-scope']").ShouldBeEmpty());
    }

    [Fact]
    public async Task Scope_all_loads_children_and_search_lazily()
    {
        // an operator without memberships: the server lists only memberships and the selection, the tree loads on demand
        Api.GetMyGroupsAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            [.. new[] { Customer, Hamburg, Bremen, Supplier }.Where(n => _selected.Contains(n.Id)).Select(n => new MyGroupDto(n.Id, n.ParentId, n.Code, n.Name, false, true))]);
        Api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns([IdentityPermissions.Groups.ScopeAll]);
        Api.GetGroupChildrenAsync(null, Arg.Any<CancellationToken>()).Returns([Customer, Supplier]);
        Api.GetGroupChildrenAsync(Customer.Id, Arg.Any<CancellationToken>()).Returns([Bremen, Hamburg]);
        Api.SearchGroupTreeAsync("Ham", Arg.Any<CancellationToken>()).Returns([Customer, Hamburg]);
        var picker = RenderPicker();
        picker.WaitForAssertion(() => Button(picker).TextContent.Trim().ShouldBe("All my groups"));
        // one look at the roots decides whether the tenant has groups at all; the tree itself loads when it opens
        await Api.Received(1).GetGroupChildrenAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>());

        await OpenAsync(picker);
        _popovers.WaitForAssertion(() => _popovers.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A", "Supplier"], ignoreOrder: false));
        await _popovers.Find($"[data-group-node='{Customer.Id}']").Closest(".mud-treeview-item-content")!.QuerySelector(".mud-treeview-item-arrow button")!.ClickAsync(new());
        _popovers.WaitForAssertion(() => _popovers.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A", "Bremen", "Hamburg", "Supplier"], ignoreOrder: false));
        _popovers.Find("input[data-testid='group-search'], [data-testid='group-search'] input").Input("Ham");
        _popovers.WaitForAssertion(() => _popovers.FindAll("[data-group-node]").Select(n => n.TextContent).ShouldBe(["Customer A", "Hamburg"], ignoreOrder: false));

        await Check(Hamburg).ChangeAsync(new ChangeEventArgs { Value = true });

        picker.WaitForAssertion(() => Api.Received(1).SelectGroupsAsync(Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { Hamburg.Id })), Arg.Any<CancellationToken>()));
        picker.WaitForAssertion(() => Button(picker).TextContent.Trim().ShouldBe("Hamburg"));
    }

    private FakeRealtimeConnection Realtime => Services.GetRequiredService<FakeRealtimeConnection>();

    private IRenderedComponent<GroupScopePicker> RenderPicker()
    {
        _popovers = Render<MudPopoverProvider>();
        return Render<GroupScopePicker>();
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<GroupScopePicker> picker) => picker.Find("[data-testid='group-scope-button']");

    private async Task OpenAsync(IRenderedComponent<GroupScopePicker> picker)
    {
        picker.WaitForElement("[data-testid='group-scope-button']");
        await Button(picker).ClickAsync(new());
        _popovers.WaitForElement("[data-testid='group-tree']");
    }

    private AngleSharp.Dom.IElement Check(GroupNodeDto node) => _popovers.Find($"[data-group-check='{node.Id}'] input, input[data-group-check='{node.Id}']");

    private static RenderFragment Columns() => builder =>
    {
        builder.OpenComponent<PropertyColumn<Gadget, string>>(0);
        builder.AddComponentParameter(1, nameof(PropertyColumn<Gadget, string>.Property), (System.Linq.Expressions.Expression<Func<Gadget, string>>)(g => g.Name));
        builder.CloseComponent();
    };
}
