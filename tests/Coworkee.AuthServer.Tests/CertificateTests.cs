using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Coworkee.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

public sealed class CertificateTests
{
    [Fact]
    public void Outside_development_the_server_refuses_to_start_without_certificates()
    {
        var error = Should.Throw<InvalidOperationException>(() => Configure([]));
        error.Message.ShouldContain("SigningCertificate");

        Configure(new() { ["Coworkee:Auth:DevelopmentCertificates"] = "true" });
        Configure(new() { ["ASPNETCORE_ENVIRONMENT"] = "Development" });
    }

    private static void Configure(Dictionary<string, string?> values) =>
        new CoworkeeAuthServerModule().ConfigureServices(new ModuleServiceContext(new ServiceCollection(), new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
}

/// <summary>Self signed PKCS#12 files for the test server, as an operator would configure them.</summary>
internal static class TestCertificates
{
    public static string Write(string name, X509KeyUsageFlags usage)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN=coworkee-test-{name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(Path.GetTempPath(), $"coworkee-test-{name}-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12));
        return path;
    }
}
