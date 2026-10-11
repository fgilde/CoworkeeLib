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

    /// <summary>Users must carry their <see cref="KeycloakUser.Password"/>.</summary>
    public static async Task ReconcileAsync(HttpClient http, string admin, string adminPassword, string realm, IReadOnlyList<Client> clients, IReadOnlyList<KeycloakUser> users, CancellationToken cancellationToken)
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
                    ["clients"] = new JsonArray([.. clients.Select(ClientJson)]),
                    ["users"] = new JsonArray([.. users.Select(u => UserJson(u, withCredentials: true))]),
                };
                await SendAsync(http.PostAsJsonAsync("admin/realms", created, cancellationToken));
                return;
            }

            await Ensure(existing);
        }

        foreach (var client in clients)
        {
            var id = await IdAsync(http, $"{root}/clients?clientId={Uri.EscapeDataString(client.ClientId)}", cancellationToken);
            await SendAsync(id is null
                ? http.PostAsJsonAsync($"{root}/clients", ClientJson(client), cancellationToken)
                : http.PutAsJsonAsync($"{root}/clients/{id}", ClientJson(client), cancellationToken));
        }

        foreach (var user in users)
        {
            var id = await IdAsync(http, $"{root}/users?exact=true&username={Uri.EscapeDataString(user.Email)}", cancellationToken);
            if (id is null)
            {
                await SendAsync(http.PostAsJsonAsync($"{root}/users", UserJson(user, withCredentials: true), cancellationToken));
                continue;
            }

            await SendAsync(http.PutAsJsonAsync($"{root}/users/{id}", UserJson(user, withCredentials: false), cancellationToken));
            await SendAsync(http.PutAsJsonAsync($"{root}/users/{id}/reset-password", Credential(user), cancellationToken));
        }
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
