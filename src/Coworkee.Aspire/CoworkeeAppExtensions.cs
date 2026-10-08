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
}
