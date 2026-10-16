using System.Net.Http.Json;
using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    /// <summary>The line the app host prints while the system waits for its setup; the Coworkee Studio reads the token from it.</summary>
    public const string SetupTokenLine = "Setup token of";

    private string? _localSetupToken;

    /// <summary>The setup token of a local run: configured (Coworkee:SetupToken of the app host) or new per run, the same for every service.</summary>
    private string LocalSetupToken => _localSetupToken ??= Builder.Configuration["Coworkee:SetupToken"] is { Length: > 0 } configured
        ? configured
        : Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_');

    private void WithSetupToken(IResourceBuilder<ProjectResource> service)
    {
        if (!Publishing)
        {
            service.WithSetting(s => s.Coworkee.SetupToken, LocalSetupToken);
        }
    }

    // once the first api runs: a system without setup gets the token on the console (coworkee run, Studio), in the api's log and as Setup link in the dashboard
    private void AnnounceSetup(IResourceBuilder<ProjectResource> api)
    {
        if (Publishing || _apis.Count > 0)
        {
            return;
        }

        api.OnResourceReady(async (resource, ready, cancellationToken) =>
        {
            try
            {
                await AnnounceSetupAsync(resource, ready.Services, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                ready.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource).LogDebug(exception, "Setup state not available");
            }
        });
    }

    private async Task AnnounceSetupAsync(ProjectResource api, IServiceProvider services, CancellationToken cancellationToken)
    {
        using var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (request, _, _, errors) => errors == 0 || request.RequestUri!.IsLoopback })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        var endpoint = api.GetEndpoints().FirstOrDefault(e => e.EndpointName == "https") ?? api.GetEndpoint("http");
        var status = await http.GetFromJsonAsync<SetupStatus>($"{endpoint.Url}/api/v1/setup/status", cancellationToken);
        if (status is not { IsInitialized: false })
        {
            return;
        }

        var token = LocalSetupToken;
        var link = Web is { } web ? $"{web.GetEndpoint("https").Url}/setup?token={Uri.EscapeDataString(token)}" : null;
        var text = $"{SetupTokenLine} {Name}: {token}" + (link is null ? string.Empty : $" - open {link}");
        services.GetRequiredService<ILoggerFactory>().CreateLogger("Coworkee.Setup").LogWarning("{SetupLine}", text);
        services.GetRequiredService<ResourceLoggerService>().GetLogger(api).LogWarning("{SetupLine}", text);
        var notifications = services.GetRequiredService<ResourceNotificationService>();
        await notifications.PublishUpdateAsync(api, s => s with
        {
            Properties = [.. s.Properties, new ResourcePropertySnapshot("coworkee.setup.token", token) { DisplayName = "Setup token", IsSensitive = true }],
        });
        if (link is not null && Web is { } webApp)
        {
            webApp.Resource.Annotations.Add(new ResourceUrlAnnotation { Url = link, DisplayText = "Setup" });
            await notifications.PublishUpdateAsync(webApp.Resource, s => s with { Urls = [.. s.Urls, new UrlSnapshot("Setup", link, IsInternal: false)] });
        }
    }

    private sealed record SetupStatus(bool IsInitialized);
}
