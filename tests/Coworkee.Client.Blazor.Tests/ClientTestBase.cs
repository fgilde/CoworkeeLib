using Bunit;
using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Api;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public abstract class ClientTestBase : BunitContext
{
    protected ClientTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
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
        Services.AddScoped<Localization.CoworkeeLocalizer>();
    }

    protected ICoworkeeApi Api { get; } = Substitute.For<ICoworkeeApi>();

    protected Localization.ILocalizationApi Localization { get; } = Substitute.For<Localization.ILocalizationApi>();
}
