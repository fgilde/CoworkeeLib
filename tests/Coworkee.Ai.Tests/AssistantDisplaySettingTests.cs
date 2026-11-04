using System.Net.Http.Json;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;

namespace Coworkee.Ai.Tests;

/// <summary>Where the assistant shows reaches every client; Ai.Enabled keeps deciding whether it is there at all.</summary>
public sealed class AssistantDisplaySettingTests(AiApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private async Task<string?> ClientValueAsync() =>
        (await Admin.GetFromJsonAsync<Dictionary<string, string?>>("/api/v1/settings/client", Ct))![AiSettingNames.Display];

    private Task<HttpResponseMessage> PutAsync(string? display) =>
        Admin.PutAsJsonAsync("/api/v1/settings/global", new SetSettingsRequest(new Dictionary<string, string?> { [AiSettingNames.Display] = display }), Ct);

    [Fact]
    public async Task The_display_defaults_to_the_floating_button_and_takes_only_known_modes()
    {
        (await ClientValueAsync()).ShouldBe(AssistantDisplay.BottomRight);

        (await PutAsync(AssistantDisplay.AppBar)).EnsureSuccessStatusCode();
        (await ClientValueAsync()).ShouldBe(AssistantDisplay.AppBar);
        (await PutAsync("Somewhere")).IsSuccessStatusCode.ShouldBeFalse();

        (await PutAsync(null)).EnsureSuccessStatusCode();
    }
}
