using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

public sealed class CustomPermissionTests(IdentityApp app) : IAsyncLifetime
{
    private const string Url = "/api/v1/identity/permissions/custom";
    private const string Export = "Custom.Reports.Export";

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task A_created_permission_is_defined_at_once_and_works_like_any_other()
    {
        (await Admin.PostAsJsonAsync(Url, new CreateCustomPermissionRequest(Export, "Export reports", "Shows the export button", null), Ct)).EnsureSuccessStatusCode();

        var definitions = (await Admin.GetFromJsonAsync<PermissionGroupDto[]>("/api/v1/identity/permissions/definitions", Ct))!;
        definitions.Single(g => g.Name == CustomPermissionNames.DefaultGroup).Permissions.ShouldContain(p => p.Name == Export && p.DisplayName == "Export reports");
        (await Admin.GetFromJsonAsync<CustomPermissionDto[]>(Url, Ct))!
            .ShouldBe([new CustomPermissionDto(Export, "Export reports", "Shows the export button", CustomPermissionNames.DefaultGroup)]);

        var user = await UserWithRoleAsync("eve@acme.test", "Exporters", Export);
        (await app.As(user, _setup.TenantId).GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct))!.ShouldContain(Export);
    }

    [Fact]
    public async Task A_permission_may_join_a_group_and_change_its_texts_but_not_its_name()
    {
        (await Admin.PostAsJsonAsync(Url, new CreateCustomPermissionRequest(Export, "Export reports", null, "Reports"), Ct)).EnsureSuccessStatusCode();

        (await Admin.PutAsJsonAsync($"{Url}/{Export}", new UpdateCustomPermissionRequest("Export all reports", "Every report", "Reporting"), Ct)).EnsureSuccessStatusCode();

        (await Admin.GetFromJsonAsync<CustomPermissionDto[]>(Url, Ct))!.ShouldBe([new CustomPermissionDto(Export, "Export all reports", "Every report", "Reporting")]);
        var definitions = (await Admin.GetFromJsonAsync<PermissionGroupDto[]>("/api/v1/identity/permissions/definitions", Ct))!;
        definitions.Single(g => g.Name == "Reporting").Permissions.Select(p => p.Name).ShouldBe([Export]);
        definitions.ShouldNotContain(g => g.Name == "Reports");
    }

    [Theory]
    [InlineData("Reports.Export")]
    [InlineData("Custom.")]
    [InlineData("Custom.Bad name")]
    [InlineData("Custom..Double")]
    public async Task Names_carry_the_custom_prefix_and_letters_digits_and_dots(string name)
    {
        var response = await Admin.PostAsJsonAsync(Url, new CreateCustomPermissionRequest(name, "Something", null, null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Names_of_code_defined_and_existing_custom_permissions_are_taken()
    {
        (await Admin.PostAsJsonAsync(Url, new CreateCustomPermissionRequest(Export, "Export reports", null, null), Ct)).EnsureSuccessStatusCode();

        foreach (var name in new[] { Export, CodedCustomPermission.Name })
        {
            var response = await Admin.PostAsJsonAsync(Url, new CreateCustomPermissionRequest(name, "Again", null, null), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await CodeAsync(response)).ShouldBe("identity.permission_taken");
        }
    }

    [Fact]
    public async Task Code_defined_permissions_can_be_neither_edited_nor_deleted()
    {
        foreach (var name in new[] { IdentityPermissions.Users.View, CodedCustomPermission.Name })
        {
            var update = await Admin.PutAsJsonAsync($"{Url}/{name}", new UpdateCustomPermissionRequest("Mine now", null, null), Ct);
            update.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await CodeAsync(update)).ShouldBe("identity.permission_code_defined");

            var delete = await Admin.DeleteAsync($"{Url}/{name}", Ct);
            delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await CodeAsync(delete)).ShouldBe("identity.permission_code_defined");
        }

        (await Admin.DeleteAsync($"{Url}/Custom.Unknown", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_permission_managers_handle_custom_permissions()
    {
        var editor = await UserWithRoleAsync("ed@acme.test", "Role viewers", IdentityPermissions.Roles.Manage);

        var response = await app.As(editor, _setup.TenantId).PostAsJsonAsync(Url, new CreateCustomPermissionRequest(Export, "Export reports", null, null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_delete_preview_names_the_roles_groups_and_users_that_lose_the_permission()
    {
        (await Admin.PostAsJsonAsync(Url, new CreateCustomPermissionRequest(Export, "Export reports", null, null), Ct)).EnsureSuccessStatusCode();
        var user = await UserWithRoleAsync("eve@acme.test", "Exporters", Export);
        await RoleAsync("Other", IdentityPermissions.Users.View);
        await PutAsync($"/api/v1/identity/permissions/grants/User/{user}", new NameListRequest([Export]));
        var group = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("Sales", null));
        await PutAsync($"/api/v1/identity/permissions/grants/Group/{group}", new NameListRequest([Export]));

        var usage = (await Admin.GetFromJsonAsync<CustomPermissionUsageDto>($"{Url}/{Export}/usage", Ct))!;

        usage.Roles.ShouldBe(["Exporters"]);
        usage.Groups.ShouldBe(["Sales"]);
        usage.Users.ShouldBe(["eve@acme.test"]);
    }

    [Fact]
    public async Task Deleting_removes_the_definition_and_every_grant_and_the_holders_lose_it_at_once()
    {
        (await Admin.PostAsJsonAsync(Url, new CreateCustomPermissionRequest(Export, "Export reports", null, null), Ct)).EnsureSuccessStatusCode();
        var user = await UserWithRoleAsync("eve@acme.test", "Exporters", Export);
        var asUser = app.As(user, _setup.TenantId);
        (await asUser.GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct))!.ShouldContain(Export);

        (await Admin.DeleteAsync($"{Url}/{Export}", Ct)).EnsureSuccessStatusCode();

        (await asUser.GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct))!.ShouldNotContain(Export);
        (await Admin.GetFromJsonAsync<CustomPermissionDto[]>(Url, Ct))!.ShouldBeEmpty();
        (await Admin.GetFromJsonAsync<PermissionGroupDto[]>("/api/v1/identity/permissions/definitions", Ct))!
            .SelectMany(g => g.Permissions).ShouldNotContain(p => p.Name == Export);
        (await app.InDbAsync(null, db => db.Set<PermissionGrant>().CountAsync(g => g.Name == Export, Ct))).ShouldBe(0);
        new IdentityRealtimeTopics().TopicsFor(new CustomPermission { Name = Export, DisplayName = "Export", Group = "Custom" })
            .ShouldBe([Contracts.Realtime.RealtimeTopics.GlobalPermissions]);
    }

    private async Task<Guid> RoleAsync(string name, params string[] permissions)
    {
        var role = await PostAsync<Guid>("/api/v1/identity/roles", new RoleRequest(name, null));
        await PutAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions));
        return role;
    }

    private async Task<Guid> UserWithRoleAsync(string email, string role, params string[] permissions)
    {
        var user = await PostAsync<UserDto>("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null));
        await PutAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([await RoleAsync(role, permissions)]));
        return user.Id;
    }

    private async Task PutAsync(string url, object body) => (await Admin.PutAsJsonAsync(url, body, Ct)).EnsureSuccessStatusCode();

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Admin.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
}
