namespace Coworkee.Contracts.Configuration;

/// <summary>The services of the app by name (<c>Coworkee:Services:myapp-api:Url</c>); the app host fills it while developing, appsettings add or override entries.</summary>
public sealed class CoworkeeServicesOptions() : Dictionary<string, CoworkeeServiceOptions>(StringComparer.OrdinalIgnoreCase)
{
    public const string Section = "Coworkee:Services";

    /// <summary>Health checks with this tag are listed as services too (e.g. "antivirus"), with their state and description.</summary>
    public const string HealthCheckTag = "coworkee-service";

    /// <summary>
    /// The Aspire dashboard (<c>Coworkee:Services:dashboard:Url</c>): the app host passes it while running and on Azure Container Apps; the Services page
    /// shows it on top while it answers.
    /// </summary>
    public const string Dashboard = "dashboard";
}
