using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    public const string OpenWebUIImage = "open-webui/open-webui";
    public const string OpenWebUITag = "v0.12.4";

    /// <summary>
    /// Open WebUI, a chat in the browser over the models of <paramref name="ollama"/>: the app's administrator signs in locally, and with the auth server
    /// the administrators of the system organisation through it, as admins of Open WebUI. A deployment gives it a public address (<c>{app}-openwebui-url</c>).
    /// Add it when <c>app.Deploys(CoworkeeTools.OpenWebUI)</c>, after <see cref="AddAuthServer{TProject}"/> (or <see cref="AddProjects"/>).
    /// </summary>
    public IResourceBuilder<ContainerResource> AddOpenWebUI<T>(IResourceBuilder<T> ollama)
        where T : ContainerResource
    {
        var ui = Builder.AddContainer(CoworkeeTools.OpenWebUI, OpenWebUIImage, OpenWebUITag)
            .WithImageRegistry("ghcr.io")
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithUrlForEndpoint("http", u => u.DisplayText = "Open WebUI")
            .WithEnvironment("OLLAMA_BASE_URL", ollama.GetEndpoint("http"))
            .WithEnvironment("WEBUI_NAME", $"{Title} chat")
            .WithEnvironment("WEBUI_SECRET_KEY", GeneratedSecret($"{Name}-openwebui-secret-key", 32))
            .WithEnvironment("WEBUI_ADMIN_EMAIL", AdminEmail)
            .WithEnvironment("WEBUI_ADMIN_PASSWORD", AdminPassword)
            .WithEnvironment("WEBUI_ADMIN_NAME", Title)
            // nobody signs up with the form; the administrators of the auth server get an account on their first sign-in
            .WithEnvironment("ENABLE_SIGNUP", "false")
            .WithEnvironment("DEFAULT_USER_ROLE", "admin")
            .WithEnvironment("ANONYMIZED_TELEMETRY", "false")
            // the app host owns the settings: Open WebUI would keep those of its first start (addresses, sign-in) in its database
            .WithEnvironment("ENABLE_PERSISTENT_CONFIG", "false")
            .WaitFor(ollama);
        if (Persistent)
        {
            ui.WithVolume($"{Name}-openwebui-data", "/app/backend/data");
        }

        var url = ToolUrl(ui);
        WithBrowserUrl(ui, "WEBUI_URL", "");
        if (AuthServer is not { } auth)
        {
            return ui;
        }

        // only administrators of the system organisation get through the auth server, so whoever comes is an admin of Open WebUI
        var (clientId, secret) = AddToolClient(CoworkeeTools.OpenWebUI, "Open WebUI", ReferenceExpression.Create($"{url}/oauth/oidc/callback"));
        ui.WithEnvironment("ENABLE_OAUTH_SIGNUP", "true")
            .WithEnvironment("OAUTH_MERGE_ACCOUNTS_BY_EMAIL", "true")
            .WithEnvironment("OAUTH_CLIENT_ID", clientId)
            .WithEnvironment("OAUTH_CLIENT_SECRET", secret)
            .WithEnvironment("OAUTH_PROVIDER_NAME", Title)
            .WithEnvironment("OAUTH_SCOPES", "openid email profile")
            // the auth server takes the code flow with PKCE only
            .WithEnvironment("OAUTH_CODE_CHALLENGE_METHOD", "S256")
            // the discovery over the container network names the sign-in page by the browser's address (Coworkee:Auth:DynamicBackchannel)
            .WithEnvironment("OPENID_PROVIDER_URL", ReferenceExpression.Create($"{PublicUrl(auth)}/.well-known/openid-configuration"))
            .WaitFor(auth);
        WithBrowserUrl(ui, "OPENID_REDIRECT_URI", "/oauth/oidc/callback");
        return ui;
    }
}
