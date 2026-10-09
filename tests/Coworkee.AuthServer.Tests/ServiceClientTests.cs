using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.AuthServer.Clients;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.AuthServer.Tests;

public sealed class ServiceClientTests(AuthApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_service_client_gets_a_token_without_a_user_and_the_permissions_of_its_roles_and_list()
    {
        var role = await RoleWithAsync("Client managers", IdentityPermissions.Clients.Manage);
        var byRole = (await SendAsync(new CreateClient(Service("reporting", roles: [role])))).Value;
        var byList = (await SendAsync(new CreateClient(Service("exporter", permissions: [IdentityPermissions.Clients.Manage])))).Value;
        var none = (await SendAsync(new CreateClient(Service("nobody")))).Value;

        var token = await TokenAsync("reporting", byRole.ClientSecret!);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Subject.ShouldBe("reporting");
        jwt.Audiences.ShouldContain("test_api");
        var me = await (await CallAsync(token, "/test/me")).Content.ReadFromJsonAsync<JsonElement>(Ct);
        me.GetProperty("clientId").GetString().ShouldBe("reporting");
        me.GetProperty("userId").ValueKind.ShouldBe(JsonValueKind.Null);
        me.GetProperty("tenantId").GetGuid().ShouldBe(_setup.TenantId);
        (await CallAsync(token, "/test/clients")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(await TokenAsync("exporter", byList.ClientSecret!), "/test/clients")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(await TokenAsync("nobody", none.ClientSecret!), "/test/clients")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Service_clients_are_confidential_and_get_neither_system_roles_nor_permissions_the_administrator_lacks()
    {
        var adminRole = await app.InDbAsync(db => db.Set<Role>().Where(r => r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id).SingleAsync(Ct));

        (await SendAsync(new CreateClient(Service("public-service") with { ClientType = "public" }))).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await SendAsync(new CreateClient(Service("root", roles: [adminRole])))).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await SendAsync(new CreateClient(Service("ghost", permissions: ["Not.A.Permission"])))).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    private static ClientRequest Service(string clientId, IReadOnlyList<Guid>? roles = null, IReadOnlyList<string>? permissions = null) =>
        new(clientId, null, "confidential", "implicit", [], [], [ClientGrantTypes.ClientCredentials], ["test_api"], roles, permissions);

    private async Task<string> TokenAsync(string clientId, string secret)
    {
        var response = await app.Browser().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
            ["scope"] = "test_api",
        }), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> CallAsync(string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await app.Browser().SendAsync(request, Ct);
    }

    private async Task<Guid> RoleWithAsync(string name, string permission)
    {
        var role = (await SendAsync(new Coworkee.Identity.Roles.CreateRole(new RoleRequest(name, null)))).Value;
        (await SendAsync(new Coworkee.Identity.Permissions.SetGrants(PermissionProviderType.Role, role, [permission]))).IsSuccess.ShouldBeTrue();
        return role;
    }

    private Task<T> SendAsync<T>(Coworkee.Application.Messaging.IRequest<T> request) => OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, request);
}
