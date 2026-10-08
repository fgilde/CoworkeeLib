using System.Text.Json;

namespace Aspire.Hosting;

/// <summary>Writes the realm import file; secrets stay placeholders that Keycloak fills from its environment.</summary>
internal static class KeycloakRealm
{
    public const string ClientSecretVariable = "COWORKEE_KEYCLOAK_CLIENT_SECRET";
    public const string UserPasswordVariable = "COWORKEE_KEYCLOAK_USER_PASSWORD";

    public static string Write(string directory, string realm, string clientId, IEnumerable<KeycloakUser> users)
    {
        Directory.CreateDirectory(directory);
        var document = new
        {
            realm,
            enabled = true,
            loginWithEmailAllowed = true,
            clients = new[]
            {
                new
                {
                    clientId,
                    enabled = true,
                    protocol = "openid-connect",
                    publicClient = false,
                    secret = $"${{{ClientSecretVariable}}}",
                    standardFlowEnabled = true,
                    directAccessGrantsEnabled = false,
                    // ponytail: any redirect for local development; a deployed realm lists the auth server callback only
                    redirectUris = new[] { "*" },
                    webOrigins = new[] { "+" },
                },
            },
            users = users.Select(u => new
            {
                username = u.Email,
                email = u.Email,
                emailVerified = true,
                enabled = true,
                firstName = u.FirstName,
                lastName = u.LastName,
                credentials = new[] { new { type = "password", value = $"${{{UserPasswordVariable}}}", temporary = false } },
            }),
        };
        File.WriteAllText(Path.Combine(directory, $"{realm}-realm.json"), JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        return directory;
    }
}
