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
    // the solution runs all test projects at once; on a busy machine the thread pool adds threads only slowly, so background
    // loads (user card batches, debounced searches) waited longer than any timeout - start with enough threads instead
    static ClientTestBase()
    {
        DefaultWaitTimeout = TimeSpan.FromSeconds(10);
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.SetMinThreads(Math.Max(workers, 64), Math.Max(io, 64));
    }

    protected ClientTestBase()
    {
        // a layout rendered by a test in parallel can switch the process-wide culture (CoworkeeLocalizer.UseAsync); every test starts in English
        TestCulture.Pin();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServicesWithExtensions();
        Components.Editors.CoworkeeEditors.Register();
        Services.AddScoped(typeof(MudBlazor.Extensions.Components.ObjectEdit.IObjectMetaConfiguration<>), typeof(Components.SettingsItemMeta<>));
        Services.AddScoped<MudBlazor.Extensions.Components.ObjectEdit.IObjectMetaConfiguration<Theming.CoworkeeTheme>, Theming.CoworkeeThemeEditMeta>();
        Services.AddScoped<Security.PermissionStore>();
        Services.AddSingleton(FeaturesApi);
        Services.AddScoped<Features.FeatureStore>();
        Services.AddScoped<People.UserCards>();
        Services.AddScoped<People.Presence>();
        Services.AddScoped<Navigation.NavigationBadges>();
        Services.AddSingleton(ChatApi);
        Services.AddSingleton(Substitute.For<Ai.IAssistantApi>());
        Services.AddScoped<Ai.AssistantState>();
        Api.GetOnlineUsersAsync(default).ReturnsForAnyArgs([]);
        Services.AddSingleton(Api);
        Services.AddSingleton(Substitute.For<Files.IFilesApi>());
        Services.AddSingleton(new CoworkeeClientOptions());
        Services.AddSingleton<Navigation.CoworkeePages>();
        Services.AddScoped<Theming.ThemeService>();
        Services.AddScoped<Layout.LayoutPreferences>();
        Services.AddScoped<Layout.PwaService>();
        Services.AddScoped<Data.FileDownloader>();
        Services.AddScoped<Data.ITableViewStore, Data.TableViews>();
        Services.AddSingleton<FakeRealtimeConnection>();
        Services.AddSingleton<Realtime.IRealtimeConnection>(sp => sp.GetRequiredService<FakeRealtimeConnection>());
        Services.AddScoped<Realtime.RealtimeClient>();
        Services.AddScoped<Realtime.NotificationCenter>();
        Services.AddSingleton(Transfer);
        Services.AddScoped<Data.DataTransferWatcher>();
        Localization.GetTextsAsync(default!, default).ReturnsForAnyArgs(call => new Contracts.Localization.TextsDto(call.Arg<string>(), new Dictionary<string, string>()));
        Services.AddSingleton(Localization);
        Services.AddSingleton<Localization.CoworkeeLocalizer>();
    }

    /// <summary>The input of a form field found by its visible label, which survives changes of the markup MudBlazor puts around it.</summary>
    protected static AngleSharp.Dom.IElement InputByLabel(AngleSharp.Dom.IElement scope, string label) =>
        scope.QuerySelector($"input[id='{scope.QuerySelectorAll("label").First(l => l.TextContent.Replace("*", string.Empty).Trim() == label).GetAttribute("for")}']")!;

    protected ICoworkeeApi Api { get; } = Substitute.For<ICoworkeeApi>();

    protected Chat.IChatApi ChatApi { get; } = Substitute.For<Chat.IChatApi>();

    protected Data.IDataTransferApi Transfer { get; } = Substitute.For<Data.IDataTransferApi>();

    protected Features.IFeaturesApi FeaturesApi { get; } = Substitute.For<Features.IFeaturesApi>();

    /// <summary>The test renderer has a small screen, so the drawer starts closed with icons only.</summary>
    protected static async Task OpenNavigationAsync(IRenderedComponent<Components.CoworkeeLayout> layout)
    {
        await layout.Find("button[aria-label='Navigation']").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find("[data-testid='nav-tools']"));
    }

    protected Localization.ILocalizationApi Localization { get; } = Substitute.For<Localization.ILocalizationApi>();
}
