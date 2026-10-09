using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Settings;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private void AddKeycloak(IResourceBuilder<ProjectResource> auth, CoworkeeKeycloakOptions options)
    {
        var realm = options.Realm ?? Name;
        var clientId = $"{Name}-auth";
        var secret = Builder.AddParameter($"{Name}-keycloak-client-secret", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
        var password = Builder.AddParameter($"{Name}-keycloak-user-password", new GenerateParameterDefault { MinLength = 16, Special = false }, secret: true, persist: true);
        var import = KeycloakRealm.Write(Path.Combine(Builder.AppHostDirectory, "obj", "keycloak"), realm, clientId, options.Users);

        Keycloak = options.Resource ?? Builder.AddKeycloak("keycloak", options.Port);
        if (Persistent && options.Resource is null)
        {
            Keycloak.WithDataVolume();
        }

        Configured(Keycloak, Options.KeycloakServer)
            .WithRealmImport(import)
            .WithEnvironment(KeycloakRealm.ClientSecretVariable, secret)
            .WithEnvironment(KeycloakRealm.UserPasswordVariable, password);

        const string Provider = "keycloak";
        auth.WaitFor(Keycloak)
            .WithSetting(s => s.Coworkee.Auth.External.Mode, options.LoginMode.ToString())
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].DisplayName, options.DisplayName)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].Authority, ReferenceExpression.Create($"{Keycloak.GetEndpoint("http")}/realms/{realm}"))
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].ClientId, clientId)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].ClientSecret, secret)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].RequireHttpsMetadata, false)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].TrustEmail, options.TrustEmail);
    }
}
