using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>clamd of the ClamAV image; its connection string is <c>tcp://host:port</c>, what Coworkee's virus scanner reads as <c>ConnectionStrings:antivirus</c>.</summary>
public sealed class ClamAvResource(string name) : ContainerResource(name), IResourceWithConnectionString
{
    public const string EndpointName = "tcp";

    /// <summary>The name services get its address under: <c>ConnectionStrings:antivirus</c>.</summary>
    public const string ConnectionStringName = "antivirus";

    public const int Port = 3310;

    public EndpointReference Endpoint => new(this, EndpointName);

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"tcp://{Endpoint.Property(EndpointProperty.Host)}:{Endpoint.Property(EndpointProperty.Port)}");
}

public static class ClamAvResourceExtensions
{
    public const string Image = "clamav/clamav";

    public const string Tag = "1.4";

    /// <summary>
    /// A ClamAV container (clamd on tcp 3310, internal: a deployment gives it TCP ingress inside the environment only). clamd takes streams up to the
    /// 2 GB of the Files module (its default of 25 MB refused larger files) and replaces its database on a signature update without a second copy in memory.
    /// While running locally it scans with the signatures of the image and skips freshclam; a deployment updates them at every start, a minute or two
    /// in which clamd answers nothing (the health check of the api shows it as degraded, uploads are refused meanwhile).
    /// <c>coworkee deploy azure</c> gives it 1.5 CPU and 3 GiB (the signatures need about 1.5 GB).
    /// </summary>
    public static IResourceBuilder<ClamAvResource> AddClamAv(this IDistributedApplicationBuilder builder, string name = "clamav")
    {
        var clamav = builder.AddResource(new ClamAvResource(name))
            .WithImage(Image, Tag)
            .WithImageRegistry("docker.io")
            .WithEndpoint(targetPort: ClamAvResource.Port, name: ClamAvResource.EndpointName, scheme: "tcp")
            .WithEnvironment("CLAMD_CONF_StreamMaxLength", "2048M")
            .WithEnvironment("CLAMD_CONF_ConcurrentDatabaseReload", "no");
        return builder.ExecutionContext.IsRunMode ? clamav.WithEnvironment("CLAMAV_NO_FRESHCLAMD", "true") : clamav;
    }
}
