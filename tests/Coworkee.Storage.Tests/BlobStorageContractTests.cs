using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.Azurite;
using Testcontainers.Minio;

namespace Coworkee.Storage.Tests;

public abstract class BlobStorageContractTests : IAsyncLifetime
{
    private ServiceProvider _services = null!;

    protected IBlobStorage Storage { get; private set; } = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public virtual async ValueTask InitializeAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(await ConfigureAsync()).Build();
        var services = new ServiceCollection();
        new CoworkeeStorageModule().ConfigureServices(new Coworkee.Core.Modularity.ModuleServiceContext(services, configuration));
        _services = services.BuildServiceProvider();
        Storage = _services.GetRequiredService<IBlobStorage>();
    }

    public virtual async ValueTask DisposeAsync() => await _services.DisposeAsync();

    protected abstract Task<Dictionary<string, string?>> ConfigureAsync();

    [Fact]
    public async Task Stored_blobs_can_be_read_back_and_deleted()
    {
        var key = BlobKeys.New(Guid.CreateVersion7(), TimeProvider.System);

        await Storage.PutAsync(key, new MemoryStream(Encoding.UTF8.GetBytes("hello blob")), "text/plain", Ct);

        (await Storage.ExistsAsync(key, Ct)).ShouldBeTrue();
        await using (var read = await Storage.OpenReadAsync(key, Ct))
        {
            new StreamReader(read!).ReadToEnd().ShouldBe("hello blob");
        }

        await Storage.DeleteAsync(key, Ct);
        (await Storage.ExistsAsync(key, Ct)).ShouldBeFalse();
        (await Storage.OpenReadAsync(key, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Overwriting_replaces_the_content_and_deleting_twice_is_fine()
    {
        var key = BlobKeys.New(Guid.CreateVersion7(), TimeProvider.System);
        await Storage.PutAsync(key, new MemoryStream([1, 2, 3]), null, Ct);
        await Storage.PutAsync(key, new MemoryStream([4]), null, Ct);

        await using (var read = await Storage.OpenReadAsync(key, Ct))
        {
            var buffer = new MemoryStream();
            await read!.CopyToAsync(buffer, Ct);
            buffer.ToArray().ShouldBe([(byte)4]);
        }

        await Storage.DeleteAsync(key, Ct);
        await Storage.DeleteAsync(key, Ct);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("a//b")]
    [InlineData("a\\b")]
    [InlineData("")]
    public async Task Unsafe_keys_are_rejected(string key) =>
        await Should.ThrowAsync<ArgumentException>(() => Storage.PutAsync(key, new MemoryStream([1]), null, Ct));
}

public sealed class FileSystemBlobStorageTests : BlobStorageContractTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coworkee-blobs-" + Guid.NewGuid().ToString("N"));

    protected override Task<Dictionary<string, string?>> ConfigureAsync() => Task.FromResult(new Dictionary<string, string?>
    {
        ["Coworkee:Storage:Provider"] = StorageProviders.FileSystem,
        ["Coworkee:Storage:FileSystem:Root"] = _root,
    });

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

public sealed class S3BlobStorageTests : BlobStorageContractTests
{
    private readonly MinioContainer _minio = new MinioBuilder("minio/minio:RELEASE.2023-01-31T02-24-19Z").Build();

    protected override async Task<Dictionary<string, string?>> ConfigureAsync()
    {
        await _minio.StartAsync();
        return new Dictionary<string, string?>
        {
            ["Coworkee:Storage:Provider"] = StorageProviders.S3,
            ["Coworkee:Storage:S3:ServiceUrl"] = _minio.GetConnectionString(),
            ["Coworkee:Storage:S3:AccessKey"] = _minio.GetAccessKey(),
            ["Coworkee:Storage:S3:SecretKey"] = _minio.GetSecretKey(),
            ["Coworkee:Storage:S3:Bucket"] = "coworkee-tests",
        };
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _minio.DisposeAsync();
    }
}

public sealed class AzureBlobStorageTests : BlobStorageContractTests
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0").WithCommand("--skipApiVersionCheck").Build();

    protected override async Task<Dictionary<string, string?>> ConfigureAsync()
    {
        await _azurite.StartAsync();
        return new Dictionary<string, string?>
        {
            ["Coworkee:Storage:Provider"] = StorageProviders.AzureBlob,
            ["Coworkee:Storage:AzureBlob:ConnectionString"] = _azurite.GetConnectionString(),
            ["Coworkee:Storage:AzureBlob:Container"] = "coworkee-tests",
        };
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _azurite.DisposeAsync();
    }
}

public sealed class BlobLinkTests
{
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    [Fact]
    public void A_link_resolves_to_its_key_until_it_expires()
    {
        var links = new BlobLinks(new EphemeralDataProtectionProvider(), _clock);

        var token = links.Create("tenant/2026/10/blob", TimeSpan.FromMinutes(5));

        links.TryResolve(token, out var key).ShouldBeTrue();
        key.ShouldBe("tenant/2026/10/blob");
        _clock.Advance(TimeSpan.FromMinutes(6));
        links.TryResolve(token, out _).ShouldBeFalse();
    }

    [Fact]
    public void Tampered_links_do_not_resolve()
    {
        var links = new BlobLinks(new EphemeralDataProtectionProvider(), _clock);
        var token = links.Create("tenant/blob", TimeSpan.FromMinutes(5));

        links.TryResolve(token[..^2] + (token[^2] == 'A' ? "B" : "A") + token[^1], out _).ShouldBeFalse();
        links.TryResolve("garbage", out _).ShouldBeFalse();
    }

    [Fact]
    public void Keys_are_tenant_and_month_scoped()
    {
        var tenant = Guid.CreateVersion7();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero));

        BlobKeys.New(tenant, clock).ShouldStartWith($"{tenant:N}/2026/03/");
    }
}
