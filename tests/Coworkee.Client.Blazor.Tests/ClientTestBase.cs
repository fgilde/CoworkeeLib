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
    protected ClientTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServicesWithExtensions();
        Services.AddScoped<Security.PermissionStore>();
        Services.AddSingleton(Api);
        Services.AddSingleton(new CoworkeeClientOptions());
        Services.AddScoped<Theming.ThemeService>();
        Services.AddScoped<Layout.LayoutPreferences>();
        Services.AddScoped<Data.FileDownloader>();
        Services.AddSingleton<FakeRealtimeConnection>();
        Services.AddSingleton<Realtime.IRealtimeConnection>(sp => sp.GetRequiredService<FakeRealtimeConnection>());
        Services.AddScoped<Realtime.RealtimeClient>();
        Localization.GetTextsAsync(default!, default).ReturnsForAnyArgs(call => new Contracts.Localization.TextsDto(call.Arg<string>(), new Dictionary<string, string>()));
        Services.AddSingleton(Localization);
        Services.AddSingleton<Localization.CoworkeeLocalizer>();
    }

    protected ICoworkeeApi Api { get; } = Substitute.For<ICoworkeeApi>();

    /// <summary>The test renderer has a small screen, so the drawer starts closed with icons only.</summary>
    protected static async Task OpenNavigationAsync(IRenderedComponent<Components.CoworkeeLayout> layout)
    {
        await layout.Find("button[aria-label='Navigation']").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find("[data-testid='nav-tools']"));
    }

    protected Localization.ILocalizationApi Localization { get; } = Substitute.For<Localization.ILocalizationApi>();
}
