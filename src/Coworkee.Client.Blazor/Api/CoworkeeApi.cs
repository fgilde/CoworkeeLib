using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Api;

internal sealed class CoworkeeApi(HttpClient http) : ICoworkeeApi
{
    private const string Identity = "api/v1/identity";

    public Task<BffUserDto> GetUserAsync(CancellationToken cancellationToken = default) => GetAsync<BffUserDto>("bff/user", cancellationToken);

    public Task<BffLogoutDto> LogoutAsync(CancellationToken cancellationToken = default) => SendAsync<BffLogoutDto>(HttpMethod.Post, "bff/logout", null, cancellationToken);

    public Task<SetupStatusDto> GetSetupStatusAsync(CancellationToken cancellationToken = default) => GetAsync<SetupStatusDto>("api/v1/setup/status", cancellationToken);

    public Task<SetupResultDto> CompleteSetupAsync(CompleteSetupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SetupResultDto>(HttpMethod.Post, "api/v1/setup/complete", request, cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetMyPermissionsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<string[]>($"{Identity}/permissions/me", cancellationToken);

    public Task<PagedResult<UserDto>> GetUsersAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<UserDto>>($"{Identity}/users?page={page.Page}&pageSize={page.PageSize}&search={Uri.EscapeDataString(page.Search ?? string.Empty)}", cancellationToken);

    public Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<UserDto>(HttpMethod.Post, $"{Identity}/users", request, cancellationToken);

    public Task UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{id}", request, cancellationToken);

    public Task SetUserRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{id}/roles", new IdListRequest(roleIds), cancellationToken);

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<RoleDto[]>($"{Identity}/roles", cancellationToken);

    public Task<Guid> CreateRoleAsync(RoleRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Guid>(HttpMethod.Post, $"{Identity}/roles", request, cancellationToken);

    public Task UpdateRoleAsync(Guid id, RoleRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/roles/{id}", request, cancellationToken);

    public Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Identity}/roles/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<PermissionGroupDto>> GetPermissionDefinitionsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<PermissionGroupDto[]>($"{Identity}/permissions/definitions", cancellationToken);

    public async Task<IReadOnlyList<string>> GetGrantsAsync(PermissionProviderType providerType, Guid providerKey, CancellationToken cancellationToken = default) =>
        await GetAsync<string[]>($"{Identity}/permissions/grants/{providerType}/{providerKey}", cancellationToken);

    public Task SetGrantsAsync(PermissionProviderType providerType, Guid providerKey, IReadOnlyList<string> names, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/permissions/grants/{providerType}/{providerKey}", new NameListRequest(names), cancellationToken);

    public Task<PagedResult<GroupDto>> GetGroupsAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<GroupDto>>($"{Identity}/groups?page={page.Page}&pageSize={page.PageSize}&search={Uri.EscapeDataString(page.Search ?? string.Empty)}", cancellationToken);

    public Task<Guid> CreateGroupAsync(GroupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Guid>(HttpMethod.Post, $"{Identity}/groups", request, cancellationToken);

    public Task UpdateGroupAsync(Guid id, GroupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/groups/{id}", request, cancellationToken);

    public Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Identity}/groups/{id}", null, cancellationToken);

    public Task SetGroupMembersAsync(Guid id, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/groups/{id}/members", new IdListRequest(userIds), cancellationToken);

    public Task SetGroupRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/groups/{id}/roles", new IdListRequest(roleIds), cancellationToken);

    public async Task<IReadOnlyList<ResourcePermissionDto>> GetResourcePermissionsAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default) =>
        await GetAsync<ResourcePermissionDto[]>($"{Identity}/resource-permissions/{resourceType}/{resourceId}", cancellationToken);

    public Task<Guid> GrantResourcePermissionAsync(string resourceType, Guid resourceId, GrantResourcePermissionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Guid>(HttpMethod.Post, $"{Identity}/resource-permissions/{resourceType}/{resourceId}", request, cancellationToken);

    public Task RevokeResourcePermissionAsync(string resourceType, Guid resourceId, Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Identity}/resource-permissions/{resourceType}/{resourceId}/{id}", null, cancellationToken);

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, url, body, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }

    private async Task SendAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, url, body, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-CSRF", "1");
        var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return response;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? code = null;
        Dictionary<string, string[]>? errors = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            code = problem.TryGetProperty("code", out var c) ? c.GetString() : null;
            errors = problem.TryGetProperty("errors", out var e) ? e.Deserialize<Dictionary<string, string[]>>() : null;
        }
        catch (JsonException)
        {
        }

        throw new ApiException((int)response.StatusCode, code, errors);
    }
}
