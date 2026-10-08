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
        WriteIfChanged(Path.Combine(directory, $"{realm}-realm.json"), JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        return directory;
    }

    // app hosts of parallel tests share the file and a running container may hold it open
    private static void WriteIfChanged(string path, string json)
    {
        if (Same(path, json))
        {
            return;
        }

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, json);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < 10)
            {
                if (Same(path, json))
                {
                    File.Delete(temp);
                    return;
                }

                Thread.Sleep(100 * attempt);
            }
        }
    }

    private static bool Same(string path, string json)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path) == json;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
