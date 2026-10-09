using Bunit;
using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Api;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Extensions;
using MudBlazor.Services;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public abstract class ClientTestBase : BunitContext
{
    // the solution runs all test projects at once; on a busy thread pool one second is too short for background loads
    static ClientTestBase() => DefaultWaitTimeout = TimeSpan.FromSeconds(10);

    protected ClientTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServicesWithExtensions();
        Components.Editors.CoworkeeEditors.Register();
        Services.AddScoped(typeof(MudBlazor.Extensions.Components.ObjectEdit.IObjectMetaConfiguration<>), typeof(Components.SettingsItemMeta<>));
        Services.AddScoped<Security.PermissionStore>();
        Services.AddSingleton(FeaturesApi);
        Services.AddScoped<Features.FeatureStore>();
        Services.AddScoped<People.UserCards>();
        Services.AddSingleton(Api);
        Services.AddSingleton(new CoworkeeClientOptions());
        Services.AddScoped<Theming.ThemeService>();
        Services.AddScoped<Layout.LayoutPreferences>();
        Services.AddScoped<Data.FileDownloader>();
        Services.AddSingleton<FakeRealtimeConnection>();
        Services.AddSingleton<Realtime.IRealtimeConnection>(sp => sp.GetRequiredService<FakeRealtimeConnection>());
        Services.AddScoped<Realtime.RealtimeClient>();
        Services.AddScoped<Realtime.NotificationCenter>();
        Localization.GetTextsAsync(default!, default).ReturnsForAnyArgs(call => new Contracts.Localization.TextsDto(call.Arg<string>(), new Dictionary<string, string>()));
        Services.AddSingleton(Localization);
        Services.AddSingleton<Localization.CoworkeeLocalizer>();
    }

    protected ICoworkeeApi Api { get; } = Substitute.For<ICoworkeeApi>();

    protected Features.IFeaturesApi FeaturesApi { get; } = Substitute.For<Features.IFeaturesApi>();

    /// <summary>The test renderer has a small screen, so the drawer starts closed with icons only.</summary>
    protected static async Task OpenNavigationAsync(IRenderedComponent<Components.CoworkeeLayout> layout)
    {
        await layout.Find("button[aria-label='Navigation']").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find("[data-testid='nav-tools']"));
    }

    protected Localization.ILocalizationApi Localization { get; } = Substitute.For<Localization.ILocalizationApi>();
}
