namespace Coworkee.Contracts.Configuration;

/// <summary>
/// The realm a deployment brings its Keycloak in line with (<c>Coworkee:KeycloakRealm</c>): the app host fills it for the migrations,
/// which reconcile it on every run. Without <see cref="Url"/> nothing happens.
/// </summary>
public sealed class KeycloakRealmOptions
{
    public const string Section = "Coworkee:KeycloakRealm";

    /// <summary>Keycloak as the migrations reach it, e.g. <c>http://keycloak:8080</c>.</summary>
    public string? Url { get; set; }

    public string AdminUser { get; set; } = "admin";

    public string? AdminPassword { get; set; }

    public string? Realm { get; set; }

    public string? DisplayName { get; set; }

    public List<RealmClient> Clients { get; set; } = [];

    public List<RealmUser> Users { get; set; } = [];

    public sealed class RealmClient
    {
        public string ClientId { get; set; } = "";

        public string Secret { get; set; } = "";

        public List<string> RedirectUris { get; set; } = [];
    }

    public sealed class RealmUser
    {
        public string Email { get; set; } = "";

        public string Password { get; set; } = "";

        public string FirstName { get; set; } = "";

        public string LastName { get; set; } = "";

        public List<string> Roles { get; set; } = [];
    }
}
