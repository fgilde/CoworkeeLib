using System.Net;
using System.Net.Sockets;
using System.Text;
using Coworkee.Contracts.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Storage.Tests;

/// <summary>The default virus scanner against a fake clamd on a local port: what it sends, how it reads the answers, and what happens without one.</summary>
public sealed class VirusScannerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_clean_file_passes_and_goes_to_clamd_as_INSTREAM_chunks()
    {
        await using var clamd = FakeClamd.Start("stream: OK");

        var result = await Scanner(clamd.Address).ScanAsync(new MemoryStream("%PDF"u8.ToArray()), "a.pdf", Ct);

        result.ShouldBe(VirusScanResult.Clean);
        result.Refused.ShouldBeFalse();
        result.RefusalFor("a.pdf").ShouldBeNull();
        var sent = await clamd.Received;
        Encoding.ASCII.GetString(sent, 0, 10).ShouldBe("zINSTREAM\0");
        sent[10..14].ShouldBe(new byte[] { 0, 0, 0, 4 }); // one chunk of four bytes, big endian
        Encoding.ASCII.GetString(sent, 14, 4).ShouldBe("%PDF");
        sent[^4..].ShouldBe(new byte[4]); // the closing zero length
    }

    [Fact]
    public async Task An_infected_file_is_refused_with_the_name_of_the_finding()
    {
        await using var clamd = FakeClamd.Start("stream: Eicar-Test-Signature FOUND");

        var result = await Scanner(clamd.Address).ScanAsync(new MemoryStream("X5O!P%@AP"u8.ToArray()), "eicar.com", Ct);

        result.Outcome.ShouldBe(VirusScanOutcome.Infected);
        result.Detail.ShouldBe("Eicar-Test-Signature");
        result.RefusalFor("eicar.com").ShouldBe("The file 'eicar.com' was refused: the virus scan found Eicar-Test-Signature.");
    }

    [Fact]
    public async Task An_error_answer_refuses_the_file_as_not_checked()
    {
        await using var clamd = FakeClamd.Start("INSTREAM size limit exceeded. ERROR");

        var result = await Scanner(clamd.Address).ScanAsync(new MemoryStream([1, 2, 3]), "big.mp4", Ct);

        result.Outcome.ShouldBe(VirusScanOutcome.Failed);
        result.RefusalFor("big.mp4").ShouldNotBeNull().ShouldContain("could not be checked for viruses");
    }

    [Fact]
    public async Task An_unreachable_clamd_refuses_the_file()
    {
        var result = await Scanner($"tcp://127.0.0.1:{FreePort()}").ScanAsync(new MemoryStream([1]), "a.pdf", Ct);

        result.Outcome.ShouldBe(VirusScanOutcome.Failed);
        result.Refused.ShouldBeTrue();
    }

    [Fact]
    public async Task An_address_that_is_none_refuses_the_file()
    {
        (await Scanner("tcp://").ScanAsync(new MemoryStream([1]), "a.pdf", Ct)).Outcome.ShouldBe(VirusScanOutcome.Failed);
    }

    [Fact]
    public async Task Without_a_configured_scanner_every_file_passes_unscanned()
    {
        var result = await Scanner(null).ScanAsync(new MemoryStream([1]), "a.pdf", Ct);

        result.ShouldBe(VirusScanResult.NotScanned);
        result.Refused.ShouldBeFalse();
    }

    [Theory]
    [InlineData("tcp://clamav:3310", "tcp://clamav:3310/")]
    [InlineData("tcp://clamav", "tcp://clamav:3310/")]
    [InlineData("clamav:3311", "tcp://clamav:3311/")]
    [InlineData("10.0.0.5", "tcp://10.0.0.5:3310/")]
    public void Addresses_take_port_3310_by_default(string value, string expected) =>
        ClamAvVirusScanner.Address(value).ShouldNotBeNull().ToString().ShouldBe(expected);

    [Fact]
    public async Task The_health_check_reports_the_version_or_degraded_while_clamd_does_not_answer()
    {
        await using var clamd = FakeClamd.Start("ClamAV 1.4.2/27463/Mon Nov 16 09:30:00 2026");
        var up = await HealthOf(clamd.Address);
        up.Status.ShouldBe(HealthStatus.Healthy);
        up.Description.ShouldBe("ClamAV 1.4.2/27463/Mon Nov 16 09:30:00 2026");
        Encoding.ASCII.GetString(await clamd.Received).ShouldBe("zVERSION\0");

        // loading its signatures: degraded, not unhealthy, so /health of the api stays up
        var starting = await HealthOf($"tcp://127.0.0.1:{FreePort()}");
        starting.Status.ShouldBe(HealthStatus.Degraded);
        starting.Description.ShouldNotBeNull().ShouldContain("signatures");
    }

    [Fact]
    public void The_module_registers_the_scanner_and_with_an_address_its_health_check_for_the_services_page()
    {
        using var without = Module(null);
        without.GetRequiredService<IVirusScanner>().ShouldBeOfType<ClamAvVirusScanner>();
        without.GetService<HealthCheckService>().ShouldBeNull();

        using var with = Module("tcp://clamav:3310");
        var registration = with.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>().Value.Registrations.ShouldHaveSingleItem();
        registration.Name.ShouldBe("antivirus");
        registration.Tags.ShouldContain(CoworkeeServicesOptions.HealthCheckTag);
    }

    [Fact]
    public void An_own_scanner_registered_first_is_kept_and_a_second_call_adds_nothing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVirusScanner, OwnScanner>();
        var configuration = Configuration("tcp://clamav:3310");
        services.AddCoworkeeVirusScanner(configuration).AddCoworkeeVirusScanner(configuration);

        services.Count(s => s.ServiceType == typeof(IVirusScanner)).ShouldBe(1);
        services.BuildServiceProvider().GetRequiredService<IVirusScanner>().ShouldBeOfType<OwnScanner>();
    }

    private static ClamAvVirusScanner Scanner(string? address) => new(Configuration(address), NullLogger<ClamAvVirusScanner>.Instance);

    private static IConfiguration Configuration(string? address) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:antivirus"] = address }).Build();

    private static Task<HealthCheckResult> HealthOf(string address) =>
        new ClamAvHealthCheck(Configuration(address)).CheckHealthAsync(new HealthCheckContext(), Ct);

    private static ServiceProvider Module(string? address)
    {
        var configuration = Configuration(address);
        var services = new ServiceCollection().AddLogging().AddSingleton(configuration);
        new CoworkeeStorageModule().ConfigureServices(new Coworkee.Core.Modularity.ModuleServiceContext(services, configuration));
        return services.BuildServiceProvider();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class OwnScanner : IVirusScanner
    {
        public Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken) => Task.FromResult(VirusScanResult.Clean);
    }
}

/// <summary>Accepts one connection, reads the command (and for INSTREAM the chunks up to the zero length), answers <c>reply\0</c> and closes, like clamd.</summary>
internal sealed class FakeClamd : IAsyncDisposable
{
    private readonly TcpListener _listener;

    private FakeClamd(string reply)
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Received = ServeAsync(reply);
    }

    public string Address => $"tcp://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public Task<byte[]> Received { get; }

    public static FakeClamd Start(string reply) => new(reply);

    private async Task<byte[]> ServeAsync(string reply)
    {
        using var client = await _listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var data = new MemoryStream();
        var buffer = new byte[8192];
        while (!Complete(data))
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            data.Write(buffer, 0, read);
        }

        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\0"));
        return data.ToArray();
    }

    // zVERSION\0 alone, zINSTREAM\0 up to the four zero bytes after at least one chunk header
    private static bool Complete(MemoryStream data)
    {
        var bytes = data.GetBuffer().AsSpan(0, (int)data.Length);
        return bytes.StartsWith("zINSTREAM\0"u8) ? bytes.Length >= 14 && bytes[^4..].SequenceEqual(new byte[4]) : bytes.IndexOf((byte)0) >= 0;
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}
