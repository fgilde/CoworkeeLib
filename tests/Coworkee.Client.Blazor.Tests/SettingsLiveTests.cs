using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Ai;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>A setting saved by anyone reaches every open page at once: the client settings reload, and what follows them changes without a reload.</summary>
public sealed class SettingsLiveTests : ClientTestBase
{
    private static readonly Guid Me = Guid.NewGuid();
    private readonly IAssistantApi _assistant = Substitute.For<IAssistantApi>();

    public SettingsLiveTests()
    {
        Services.AddSingleton(_assistant);
        Services.AddScoped<AssistantState>();
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([AiPermissions.Chat]);
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Me.ToString())).SetPolicies(Security.PermissionPolicy.For(AiPermissions.Chat));
    }

    private FakeRealtimeConnection Realtime => Services.GetRequiredService<FakeRealtimeConnection>();

    private ThemeService Themes => Services.GetRequiredService<ThemeService>();

    [Theory]
    [InlineData(SettingTopics.Global)]
    [InlineData(SettingTopics.Tenant)]
    public void A_setting_saved_elsewhere_reloads_the_client_settings(string topic)
    {
        Api.GetClientSettingsAsync(default).ReturnsForAnyArgs(new Dictionary<string, string?> { ["Table.Mode"] = "Pager" });
        var live = Render<SettingsLiveUpdates>();
        live.InvokeAsync(Themes.LoadAsync);
        Api.GetClientSettingsAsync(default).ReturnsForAnyArgs(new Dictionary<string, string?> { ["Table.Mode"] = "EndlessScroll" });

        Realtime.Push(topic);

        live.WaitForAssertion(() => Themes.ClientSettings["Table.Mode"].ShouldBe("EndlessScroll"));
    }

    [Fact]
    public void A_setting_of_the_user_reloads_them_and_other_events_of_the_user_do_not()
    {
        Render<SettingsLiveUpdates>();
        Api.ClearReceivedCalls();

        Realtime.Push(RealtimeTopics.User(Me), RealtimeEventTypes.EntityChanged, new EntityChangedPayload("Notification", "1", EntityChangedPayload.Created, []));
        Api.DidNotReceiveWithAnyArgs().GetClientSettingsAsync(default);
        Realtime.Push(RealtimeTopics.User(Me), RealtimeEventTypes.EntityChanged, new EntityChangedPayload("SettingValue", "2", EntityChangedPayload.Updated, []));

        Api.ReceivedWithAnyArgs().GetClientSettingsAsync(default);
    }

    [Fact]
    public void The_assistant_appears_and_goes_when_it_is_switched_on_and_off()
    {
        _assistant.GetStatusAsync(default).ReturnsForAnyArgs(new AiStatusDto(false));
        Render<SettingsLiveUpdates>();
        var launcher = Render<AssistantLauncher>();
        launcher.FindAll("[data-testid='assistant-fab']").ShouldBeEmpty();

        _assistant.GetStatusAsync(default).ReturnsForAnyArgs(new AiStatusDto(true));
        Realtime.Push(SettingTopics.Tenant);
        launcher.WaitForAssertion(() => launcher.Find("[data-testid='assistant-fab']"));

        _assistant.GetStatusAsync(default).ReturnsForAnyArgs(new AiStatusDto(false));
        Realtime.Push(SettingTopics.Global);
        launcher.WaitForAssertion(() => launcher.FindAll("[data-testid='assistant-fab']").ShouldBeEmpty());
    }
}
