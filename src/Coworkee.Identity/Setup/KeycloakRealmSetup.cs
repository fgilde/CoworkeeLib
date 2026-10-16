using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Setup;

public static class KeycloakRealmSetup
{
    /// <summary>
    /// Brings the deployed Keycloak's realm in line with <see cref="KeycloakRealmOptions"/> (clients, the initial administrator); nothing without it.
    /// Keycloak starts slower than the migrations, so it is retried for a while.
    /// </summary>
    public static async Task ReconcileKeycloakRealmAsync(this IServiceProvider services, int attempts = 40, CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<IConfiguration>().GetSection(KeycloakRealmOptions.Section).Get<KeycloakRealmOptions>();
        if (options is not { Url.Length: > 0, Realm.Length: > 0 })
        {
            return;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(KeycloakRealmSetup));
        var clients = options.Clients.Select(c => new KeycloakRealm.Client(c.ClientId, c.Secret, c.RedirectUris)).ToList();
        var users = options.Users.Where(u => u.Email.Length > 0 && u.Password.Length > 0)
            .Select(u => new KeycloakRealm.User(u.Email, u.FirstName, u.LastName, u.Password, u.Roles)).ToList();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(options.Url.TrimEnd('/') + "/") };
                await KeycloakRealm.ReconcileAsync(http, options.AdminUser, options.AdminPassword ?? "", options.Realm, new(options.DisplayName ?? options.Realm, null), clients, users,
                    cancellationToken);
                logger.LogInformation("Realm {Realm} is up to date: clients {Clients}", options.Realm, string.Join(", ", clients.Select(c => c.ClientId)));
                return;
            }
            catch (HttpRequestException exception) when (attempt < attempts)
            {
                logger.LogWarning("Keycloak not ready yet ({Message}), attempt {Attempt}", exception.Message, attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }
}
