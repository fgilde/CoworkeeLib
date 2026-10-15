using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Workflows;

namespace Coworkee.Workflows.Elsa.Tests;

public sealed class ElsaTests(ElsaApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Only_users_with_the_permission_reach_the_Elsa_api_and_see_the_Coworkee_activities()
    {
        var viewer = await CreateUserAsync("viewer@acme.test", "Viewers", WorkflowPermissions.View);

        (await app.App.GetTestClient().GetAsync("/api/elsa/workflow-definitions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await app.As(viewer, _setup.TenantId).GetAsync("/api/elsa/workflow-definitions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var descriptors = await Admin.GetStringAsync("/api/elsa/descriptors/activities", Ct);

        descriptors.ShouldContain("Coworkee.WaitForApproval");
        descriptors.ShouldContain("Coworkee.WorkflowStateReached");
        var studio = await Admin.GetAsync("/api/v1/workflows/elsa/studio", Ct);
        (studio.StatusCode, studio.Headers.Location?.ToString()).ShouldBe((HttpStatusCode.Redirect, "https://studio.example/"));
    }

    [Fact]
    public async Task A_transition_starts_an_Elsa_workflow_which_waits_for_the_decision()
    {
        var reviewer = await CreateReviewerAsync("reviewer@acme.test");
        var document = await app.AddDocumentAsync(_setup.TenantId);
        await app.DrainOutboxAsync();

        var approval = (await (await Admin.PostAsync($"/api/v1/workflows/Documents/{document}/Submit", null, Ct)).Content.ReadFromJsonAsync<WorkflowStatusDto>(Ct))!.Approval!;
        await app.DrainOutboxAsync();

        (await TitlesAsync(reviewer)).ShouldContain("A document waits for review");
        (await TitlesAsync(reviewer)).ShouldNotContain("The review is decided");

        (await app.As(reviewer, _setup.TenantId).PostAsJsonAsync($"/api/v1/workflows/approvals/{approval.Id}", new DecisionRequest(true), Ct)).EnsureSuccessStatusCode();
        await app.DrainOutboxAsync();

        (await TitlesAsync(reviewer)).ShouldContain("The review is decided");
    }

    [Fact]
    public async Task A_new_entity_of_a_watched_type_starts_a_workflow()
    {
        var reviewer = await CreateReviewerAsync("newdocs@acme.test");

        await app.AddDocumentAsync(_setup.TenantId);
        await app.DrainOutboxAsync();

        (await TitlesAsync(reviewer)).ShouldContain("A new document");
    }

    private async Task<List<string>> TitlesAsync(Guid userId) => [.. (await app.NotificationsAsync()).Where(n => n.UserId == userId).Select(n => n.Title)];

    private async Task<Guid> CreateUserAsync(string email, string role, params string[] permissions)
    {
        var user = await ReadAsync<UserDto>(await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct));
        var roles = (await Admin.GetFromJsonAsync<List<RoleDto>>("/api/v1/identity/roles", Ct))!;
        var roleId = roles.FirstOrDefault(r => r.Name == role)?.Id ?? await ReadAsync<Guid>(await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(role, null), Ct));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{roleId}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([roleId]), Ct)).EnsureSuccessStatusCode();
        return user.Id;
    }

    private Task<Guid> CreateReviewerAsync(string email) => CreateUserAsync(email, "Reviewer", WorkflowPermissions.View);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
