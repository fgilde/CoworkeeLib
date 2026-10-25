using Aspire.Hosting.ApplicationModel;
using Nextended.Aspire.Hosting.AspireUI;

namespace Aspire.Hosting;

public static class CoworkeeAppExtensions
{
    /// <summary>Configuration switch (e.g. <c>--Coworkee:EphemeralInfrastructure=true</c>) that skips the data volumes, for tests and demos.</summary>
    public const string EphemeralSetting = "Coworkee:EphemeralInfrastructure";

    /// <summary>
    /// Starts a Coworkee application: <c>builder.AddCoworkeeApp("myapp")</c>, then AddMigrations, AddAuthServer, AddApi (and AddWorker), AddWeb.
    /// </summary>
    public static CoworkeeApp AddCoworkeeApp(this IDistributedApplicationBuilder builder, string name, Action<CoworkeeAppOptions>? configure = null)
    {
        var options = new CoworkeeAppOptions();
        configure?.Invoke(options);
        return new CoworkeeApp(builder, name, options);
    }

    /// <summary>
    /// Sign-in to AspireUI at the app's auth server: a client of its own, administrators of the system organisation only, as AspireUI admins.
    /// Call it after <see cref="CoworkeeApp.AddAuthServer{TProject}"/>.
    /// </summary>
    public static IResourceBuilder<AspireUIResource> WithCoworkeeSignOn(this IResourceBuilder<AspireUIResource> aspireUi, CoworkeeApp app, string defaultPermissions = AspireUIPermissions.AppUser) =>
        app.AddAspireUISignIn(aspireUi, defaultPermissions);

    /// <summary>
    /// AspireUI as a tool of the app: the app's administrator signs in (with the auth server also through it), and a deployment gives it a public address
    /// (<c>{app}-aspireui-url</c>); its volume is the app's own. Add it when <c>app.Deploys(CoworkeeTools.AspireUI)</c>.
    /// </summary>
    public static IResourceBuilder<AspireUIResource> WithCoworkee(this IResourceBuilder<AspireUIResource> aspireUi, CoworkeeApp app, string defaultPermissions = AspireUIPermissions.AppUser) =>
        app.AddAspireUI(aspireUi, defaultPermissions);
}
