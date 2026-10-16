using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Workflows;

namespace Coworkee.Workflows.Tests;

public sealed class WorkflowODataTests(WorkflowApp app) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Rows_filter_sort_and_facet_by_their_workflow_state_and_never_by_a_state_of_another_tenant()
    {
        var (alpha, beta, gamma) = (await app.AddTicketAsync(_setup.TenantId, "Alpha"), await app.AddTicketAsync(_setup.TenantId, "Beta"), await app.AddTicketAsync(_setup.TenantId, "Gamma"));
        (await Admin.PostAsync($"/api/v1/workflows/Tickets/{beta}/Submit", null, Ct)).EnsureSuccessStatusCode();
        await app.AddWorkflowItemAsync(Guid.NewGuid(), "Tickets", gamma, "Done");

        (await TitlesAsync(Admin, "$filter=WorkflowState eq 'Review'")).ShouldBe(["Beta"]);
        (await TitlesAsync(Admin, "$filter=WorkflowState eq 'Open'&$orderby=Title")).ShouldBe(["Alpha", "Gamma"]);
        (await TitlesAsync(Admin, "$filter=WorkflowState eq 'Done'")).ShouldBeEmpty();
        (await TitlesAsync(Admin, "$orderby=WorkflowState desc,Title")).ShouldBe(["Beta", "Alpha", "Gamma"]);

        var page = await PageAsync(Admin, "$filter=WorkflowState eq 'Review'", facets: true);
        page.GetProperty("value")[0].TryGetProperty("WorkflowState", out _).ShouldBeFalse();
        var facet = page.GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(Json)!.Single(f => f.Key == "WorkflowState");
        facet.Options.Select(o => (o.Label, o.Count)).ShouldBe([("Open", 0), ("Review", 1), ("Done", 0), ("Rejected", 0), ("Awaiting my approval", 0)]);
        facet.Options.Single(o => o.Label == "Done").Meta!["color"].ShouldBe("#2e7d32");

        var disjunctive = await PageAsync(Admin, "$filter=WorkflowState eq 'Review'", facets: true,
            context: new FacetContextDto(null, new Dictionary<string, string> { ["WorkflowState"] = "WorkflowState eq 'Review'" }));
        disjunctive.GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(Json)!.Single(f => f.Key == "WorkflowState")
            .Options.Where(o => o.Count > 0).Select(o => (o.Label, o.Count)).ShouldBe([("Open", 2), ("Review", 1)]);
    }

    [Fact]
    public async Task Awaiting_my_approval_finds_the_rows_the_user_still_has_to_decide()
    {
        var manager = await ManagerAsync();
        var first = await app.AddTicketAsync(_setup.TenantId, "First");
        await app.AddTicketAsync(_setup.TenantId, "Second");
        (await Admin.PostAsync($"/api/v1/workflows/Tickets/{first}/Submit", null, Ct)).EnsureSuccessStatusCode();

        (await TitlesAsync(app.As(manager, _setup.TenantId), "$filter=WorkflowAwaitsMe eq true")).ShouldBe(["First"]);
        (await TitlesAsync(Admin, "$filter=WorkflowAwaitsMe eq true")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_mirrored_workflow_filters_on_its_column_and_users_without_workflow_rights_cannot_use_the_state()
    {
        var expense = await app.AddExpenseAsync(_setup.TenantId, title: "Mirrored");
        await app.AddExpenseAsync(_setup.TenantId, title: "Untouched");
        (await Admin.PostAsync($"/api/v1/workflows/Expenses/{expense}/Submit", null, Ct)).EnsureSuccessStatusCode();

        (await TitlesAsync(Admin, "$filter=WorkflowState eq 'Review'", "Expenses")).ShouldBe(["Mirrored"]);
        (await TitlesAsync(Admin, "$filter=WorkflowState eq 'Draft'", "Expenses")).ShouldBe(["Untouched"]);
        (await PageAsync(Admin, "$top=1", facets: true, set: "Expenses")).GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(Json)!.ShouldNotContain(f => f.Key == "WorkflowState");

        var reader = await UserAsync("reader@acme.test", await RoleAsync("Readers"));
        (await app.As(reader, _setup.TenantId).GetAsync("/odata/Tickets?$filter=WorkflowState eq 'Open'", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.As(reader, _setup.TenantId).GetAsync("/odata/Tickets?$orderby=WorkflowState", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var page = await PageAsync(app.As(reader, _setup.TenantId), "$top=1", facets: true);
        page.GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(Json)!.ShouldNotContain(f => f.Key == "WorkflowState");
    }

    private async Task<List<string>> TitlesAsync(HttpClient client, string query, string set = "Tickets") =>
        [.. (await PageAsync(client, query, set: set)).GetProperty("value").EnumerateArray().Select(r => r.GetProperty("Title").GetString()!)];

    private static async Task<JsonElement> PageAsync(HttpClient client, string query, bool facets = false, FacetContextDto? context = null, string set = "Tickets")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/odata/{set}?{query}");
        if (facets)
        {
            request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
        }

        if (context is not null)
        {
            request.Headers.Add(FacetContextDto.Header, context.Encode());
        }

        var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<Guid> ManagerAsync() => await UserAsync("manager@acme.test", await RoleAsync("Manager", WorkflowPermissions.View));

    private async Task<Guid> RoleAsync(string name, params string[] permissions)
    {
        var role = await ReadAsync<Guid>(await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return role;
    }

    private async Task<Guid> UserAsync(string email, Guid role)
    {
        var user = await ReadAsync<UserDto>(await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user.Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
