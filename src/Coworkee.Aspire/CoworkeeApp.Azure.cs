using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Expressions;
using Microsoft.Extensions.Configuration;

namespace Aspire.Hosting;

/// <summary>
/// What Azure Container Apps needs beyond Aspire's defaults, found in the first real deployment: every parameter starts empty (the publisher would
/// wait for a value), the services that keep files share one file share, secret names lose their dots, containers with state run as one replica,
/// and Azure Files mounts skip byte-range locks (SQLite on SMB never gets its lock otherwise).
/// </summary>
public sealed partial class CoworkeeApp
{
    private void AddAzureEnvironment()
    {
        // before the environment, so these run ahead of the Container Apps infrastructure that reads the annotations
        Builder.Eventing.Subscribe<BeforeStartEvent>((_, _) =>
        {
            StartParametersEmpty();
            // the annotations PublishAsAzureContainerApp(Job) would add: the infrastructure is registered already and the services are read-only by now
            foreach (var resource in Builder.Resources.OfType<ContainerResource>())
            {
                resource.Annotations.Add(new AzureContainerAppCustomizationAnnotation((_, app) => ConfigureContainerApp(app, stateful: true)));
            }

            foreach (var resource in Builder.Resources.OfType<ProjectResource>())
            {
                // a job (the migrations, PublishAsAzureContainerAppJob) takes the job's annotation only
                resource.Annotations.Add(resource.Annotations.OfType<AzureContainerAppJobCustomizationAnnotation>().Any()
                    ? new AzureContainerAppJobCustomizationAnnotation((_, job) => ConfigureJob(job))
                    : new AzureContainerAppCustomizationAnnotation((_, app) => ConfigureContainerApp(app, stateful: false)));
            }

            return Task.CompletedTask;
        });

        // compact names keep the unique suffix of the managed storages, which the 24 characters cut otherwise (two services' volumes got one name)
#pragma warning disable ASPIREACANAMING001
        Builder.AddAzureContainerAppEnvironment("aca").WithCompactResourceNaming().ConfigureInfrastructure(ShareFileVolume);
#pragma warning restore ASPIREACANAMING001
    }

    // the app host's own parameters too: without a value in the configuration the publisher waits for one, silently
    private void StartParametersEmpty()
    {
        foreach (var parameter in Builder.Resources.OfType<ParameterResource>().Where(p => p.Default is null))
        {
            var key = parameter.IsConnectionString ? $"ConnectionStrings:{parameter.Name}" : $"Parameters:{parameter.Name}";
            if (Builder.Configuration[key] is null)
            {
                parameter.Default = new EmptyParameterDefault();
            }
        }
    }

    internal static void ConfigureContainerApp(ContainerApp app, bool stateful)
    {
        RenameSecrets(app.Configuration.Secrets, app.Template.Containers);
        SkipLocks(app.Template.Volumes);

        // a database, cache, broker or tool keeps its data on one volume: a second replica would corrupt it
        if (stateful)
        {
            app.Template.Scale.MinReplicas = 1;
            app.Template.Scale.MaxReplicas = 1;
        }
    }

    internal static void ConfigureJob(ContainerAppJob job)
    {
        RenameSecrets(job.Configuration.Secrets, job.Template.Containers);
        SkipLocks(job.Template.Volumes);
    }

    // Container Apps take lower case letters, digits and '-' in secret names; settings like Mail.Smtp.Password have dots
    private static void RenameSecrets(IEnumerable<BicepValue<ContainerAppWritableSecret>> secrets, IEnumerable<BicepValue<ContainerAppContainer>> containers)
    {
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var secret in secrets.Select(s => s.Value).OfType<ContainerAppWritableSecret>())
        {
            if (secret.Name.Value is { } name && name.Contains('.'))
            {
                renamed[name] = name.Replace('.', '-');
                secret.Name = renamed[name];
            }
        }

        foreach (var variable in containers.Select(c => c.Value).OfType<ContainerAppContainer>().SelectMany(c => c.Env.Select(e => e.Value)).OfType<ContainerAppEnvironmentVariable>())
        {
            if (variable.SecretRef.Value is { } reference && renamed.TryGetValue(reference, out var name))
            {
                variable.SecretRef = name;
            }
        }
    }

    // SMB byte-range locks never reach SQLite on Azure Files (Grafana looped on SQLITE_BUSY); one replica writes, so the locks are not needed
    private static void SkipLocks(IEnumerable<BicepValue<ContainerAppVolume>> volumes)
    {
        foreach (var volume in volumes.Select(v => v.Value).OfType<ContainerAppVolume>())
        {
            if (volume.StorageType.Value == ContainerAppStorageType.AzureFile)
            {
                volume.MountOptions = "nobrl";
            }
        }
    }

    // Aspire gives every service its own share for a volume; the files of the app (WithFileVolume) are one store the services share
    private void ShareFileVolume(AzureResourceInfrastructure infrastructure)
    {
        var volumes = Builder.Resources.OfType<ProjectResource>()
            .Select(r => (Resource: r, Index: r.Annotations.OfType<ContainerMountAnnotation>().Where(m => m.Type == ContainerMountType.Volume).ToList().FindIndex(m => m.Source == FilesVolume)))
            .Where(s => s.Index >= 0)
            .Select(s => $"volumes_{s.Resource.Name.Replace('-', '_')}_{s.Index}")
            .ToList();
        var resources = infrastructure.GetProvisionableResources().ToList();
        var storages = resources.OfType<ContainerAppManagedEnvironmentStorage>()
            .Where(s => volumes.Any(v => s.BicepIdentifier == $"managedStorage_{v}")).OrderBy(s => volumes.FindIndex(v => s.BicepIdentifier == $"managedStorage_{v}")).ToList();
        var account = resources.OfType<global::Azure.Provisioning.Storage.StorageAccount>().FirstOrDefault();
        var share = resources.OfType<global::Azure.Provisioning.Storage.FileShare>().FirstOrDefault(r => r.BicepIdentifier == $"shares_{volumes.FirstOrDefault()}");
        if (storages.Count < 2 || account is null || share is null)
        {
            return;
        }

        // the whole azureFile block from the account and the first service's share, as Aspire writes it (the key of another storage cannot be read back)
        foreach (var storage in storages.Skip(1))
        {
            storage.Properties.AzureFile = new ContainerAppAzureFileProperties
            {
                AccountName = account.Name, AccountKey = AccountKey(account), AccessMode = ContainerAppAccessMode.ReadWrite, ShareName = share.Name,
            };
        }
    }

    // {account}.listKeys().keys[0].value: the expression Aspire writes for its own storages
    private static BicepValue<string> AccountKey(global::Azure.Provisioning.Storage.StorageAccount account)
    {
        var keys = new MemberExpression(new FunctionCallExpression(new MemberExpression(new IdentifierExpression(account.BicepIdentifier), "listKeys")), "keys");
        return new BicepValue<string>(new MemberExpression(new IndexExpression(keys, new IntLiteralExpression(0)), "value"));
    }

    private string FilesVolume => $"{Name}-files";

    private sealed class EmptyParameterDefault : ParameterDefault
    {
        public override string GetDefaultValue() => string.Empty;

        public override void WriteToManifest(Aspire.Hosting.Publishing.ManifestPublishingContext context) => context.Writer.WriteString("value", string.Empty);
    }
}
