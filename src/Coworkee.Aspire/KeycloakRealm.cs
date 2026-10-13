using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Aspire.Hosting;

/// <summary>
/// Brings the realm in line with the app host on every start: creates it, or updates the clients and users of an existing one,
/// so changed secrets, redirect addresses and passwords also reach a realm kept in the data volume.
/// </summary>
internal static class KeycloakRealm
{
    public sealed record Client(string ClientId, string Secret, IReadOnlyList<string> RedirectUris);

    /// <summary>The realm's name above the sign-in form and its login theme; without one the realm keeps the theme it has.</summary>
    public sealed record Look(string DisplayName, string? LoginTheme);

    /// <summary>Users must carry their <see cref="KeycloakUser.Password"/>.</summary>
    public static async Task ReconcileAsync(HttpClient http, string admin, string adminPassword, string realm, Look look, IReadOnlyList<Client> clients, IReadOnlyList<KeycloakUser> users,
        CancellationToken cancellationToken)
    {
        http.DefaultRequestHeaders.Authorization = new("Bearer", await TokenAsync(http, admin, adminPassword, cancellationToken));
        var root = $"admin/realms/{Uri.EscapeDataString(realm)}";
        using (var existing = await http.GetAsync(root, cancellationToken))
        {
            if (existing.StatusCode == HttpStatusCode.NotFound)
            {
                var created = new JsonObject
                {
                    ["realm"] = realm,
                    ["enabled"] = true,
                    ["loginWithEmailAllowed"] = true,
                    ["displayName"] = look.DisplayName,
                    ["loginTheme"] = look.LoginTheme,
                    ["clients"] = new JsonArray([.. clients.Select(ClientJson)]),
                    ["roles"] = new JsonObject { ["realm"] = new JsonArray([.. users.SelectMany(u => u.Roles).Distinct().Select(r => new JsonObject { ["name"] = r })]) },
                    ["users"] = new JsonArray([.. users.Select(u => UserJson(u, withCredentials: true))]),
                };
                await SendAsync(http.PostAsJsonAsync("admin/realms", created, cancellationToken));
                return;
            }

            await Ensure(existing);
        }

        // a partial representation changes only the fields it carries
        await SendAsync(http.PutAsJsonAsync(root, new JsonObject { ["displayName"] = look.DisplayName, ["loginTheme"] = look.LoginTheme }, cancellationToken));

        foreach (var client in clients)
        {
            var id = await IdAsync(http, $"{root}/clients?clientId={Uri.EscapeDataString(client.ClientId)}", cancellationToken);
            if (id is null)
            {
                await SendAsync(http.PostAsJsonAsync($"{root}/clients", ClientJson(client), cancellationToken));
                continue;
            }

            await SendAsync(http.PutAsJsonAsync($"{root}/clients/{id}", ClientJson(client), cancellationToken));
            // an update leaves the mappers alone, so a client of an older realm gets the roles claim here
            var mappers = await http.GetFromJsonAsync<JsonArray>($"{root}/clients/{id}/protocol-mappers/models", cancellationToken);
            if (mappers?.Any(m => m?["name"]?.GetValue<string>() == RolesMapper["name"]!.GetValue<string>()) != true)
            {
                await SendAsync(http.PostAsJsonAsync($"{root}/clients/{id}/protocol-mappers/models", RolesMapper, cancellationToken));
            }
        }

        foreach (var role in users.SelectMany(u => u.Roles).Distinct())
        {
            using var created = await http.PostAsJsonAsync($"{root}/roles", new JsonObject { ["name"] = role }, cancellationToken);
            if (created.StatusCode != HttpStatusCode.Conflict)
            {
                await Ensure(created);
            }
        }

        foreach (var user in users)
        {
            var id = await IdAsync(http, $"{root}/users?exact=true&username={Uri.EscapeDataString(user.Email)}", cancellationToken);
            if (id is null)
            {
                await SendAsync(http.PostAsJsonAsync($"{root}/users", UserJson(user, withCredentials: true), cancellationToken));
                id = await IdAsync(http, $"{root}/users?exact=true&username={Uri.EscapeDataString(user.Email)}", cancellationToken);
            }
            else
            {
                await SendAsync(http.PutAsJsonAsync($"{root}/users/{id}", UserJson(user, withCredentials: false), cancellationToken));
                await SendAsync(http.PutAsJsonAsync($"{root}/users/{id}/reset-password", Credential(user), cancellationToken));
            }

            await AssignRolesAsync(http, $"{root}/users/{id}/role-mappings/realm", user, cancellationToken);
        }
    }

    private static JsonObject RolesMapper => new()
    {
        ["name"] = "realm roles",
        ["protocol"] = "openid-connect",
        ["protocolMapper"] = "oidc-usermodel-realm-role-mapper",
        ["config"] = new JsonObject
        {
            ["claim.name"] = "roles", ["multivalued"] = "true", ["jsonType.label"] = "String",
            ["id.token.claim"] = "true", ["access.token.claim"] = "true", ["userinfo.token.claim"] = "true",
        },
    };

    /// <summary>The user's realm roles become exactly <see cref="KeycloakUser.Roles"/>, besides Keycloak's own defaults.</summary>
    private static async Task AssignRolesAsync(HttpClient http, string mappings, KeycloakUser user, CancellationToken cancellationToken)
    {
        var available = await http.GetFromJsonAsync<JsonArray>($"{mappings}/available", cancellationToken) ?? [];
        var assigned = await http.GetFromJsonAsync<JsonArray>(mappings, cancellationToken) ?? [];
        var add = available.Where(r => user.Roles.Contains(Name(r))).Select(r => r!.DeepClone()).ToArray();
        var remove = assigned.Where(r => !user.Roles.Contains(Name(r)) && Name(r) is not ("offline_access" or "uma_authorization") && !Name(r).StartsWith("default-roles-", StringComparison.Ordinal))
            .Select(r => r!.DeepClone()).ToArray();
        if (add.Length > 0)
        {
            await SendAsync(http.PostAsJsonAsync(mappings, new JsonArray(add), cancellationToken));
        }

        if (remove.Length > 0)
        {
            await SendAsync(http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, mappings) { Content = JsonContent.Create(new JsonArray(remove)) }, cancellationToken));
        }

        static string Name(JsonNode? role) => role?["name"]?.GetValue<string>() ?? "";
    }

    private static async Task<string> TokenAsync(HttpClient http, string admin, string password, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["client_id"] = "admin-cli", ["username"] = admin, ["password"] = password,
        });
        using var response = await http.PostAsync("realms/master/protocol/openid-connect/token", form, cancellationToken);
        await Ensure(response);
        var token = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return token!["access_token"]!.GetValue<string>();
    }

    private static async Task<string?> IdAsync(HttpClient http, string query, CancellationToken cancellationToken)
    {
        var found = await http.GetFromJsonAsync<JsonArray>(query, cancellationToken);
        return found?.FirstOrDefault()?["id"]?.GetValue<string>();
    }

    private static JsonObject ClientJson(Client client) => new()
    {
        ["clientId"] = client.ClientId,
        ["enabled"] = true,
        ["protocol"] = "openid-connect",
        ["publicClient"] = false,
        ["secret"] = client.Secret,
        ["standardFlowEnabled"] = true,
        ["directAccessGrantsEnabled"] = false,
        // ponytail: any redirect for local development unless the client names its own; a deployed realm lists the callbacks only
        ["redirectUris"] = new JsonArray([.. (client.RedirectUris.Count > 0 ? client.RedirectUris : ["*"]).Select(u => JsonValue.Create(u))]),
        ["webOrigins"] = new JsonArray("+"),
        ["attributes"] = new JsonObject { ["post.logout.redirect.uris"] = "+" },
        ["protocolMappers"] = new JsonArray(RolesMapper),
    };

    private static JsonObject UserJson(KeycloakUser user, bool withCredentials)
    {
        var json = new JsonObject
        {
            ["username"] = user.Email,
            ["email"] = user.Email,
            ["emailVerified"] = true,
            ["enabled"] = true,
            ["firstName"] = user.FirstName,
            ["lastName"] = user.LastName,
            ["requiredActions"] = new JsonArray(),
            ["realmRoles"] = new JsonArray([.. user.Roles.Select(r => JsonValue.Create(r))]),
        };
        if (withCredentials)
        {
            json["credentials"] = new JsonArray(Credential(user));
        }

        return json;
    }

    private static JsonObject Credential(KeycloakUser user) =>
        new() { ["type"] = "password", ["value"] = user.Password ?? throw new InvalidOperationException($"{user.Email} has no password."), ["temporary"] = false };

    private static async Task SendAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        await Ensure(response);
    }

    private static async Task Ensure(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Keycloak answered {(int)response.StatusCode} to {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: {await response.Content.ReadAsStringAsync()}",
                null, response.StatusCode);
        }
    }
}
