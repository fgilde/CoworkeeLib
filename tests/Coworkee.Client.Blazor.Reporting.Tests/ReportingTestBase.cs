using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Extensions;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

public abstract class ReportingTestBase : BunitContext
{
    protected static readonly ReportSourceDto Orders = new("SalesOrders", "Sales orders",
    [
        new ReportFieldDto("Status", "Status", ReportFieldKind.Enum, ["Open", "Shipped"], "Shop.Status"),
        new ReportFieldDto("Region", "Region", ReportFieldKind.Text),
        new ReportFieldDto("OrderDate", "Order date", ReportFieldKind.Date),
        new ReportFieldDto("Total", "Total", ReportFieldKind.Number),
        new ReportFieldDto("Customer/Name", "Customer name", ReportFieldKind.Text),
    ]);

    static ReportingTestBase() => DefaultWaitTimeout = TimeSpan.FromSeconds(10);

    protected ReportingTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Grid = JSInterop.SetupModule(ReportGrid.Module);
        Grid.Mode = JSRuntimeMode.Loose;
        Services.AddMudServicesWithExtensions();
        AddAuthorization().SetAuthorized("ada@acme.test");
        Services.AddSingleton(Reports);
        Services.AddSingleton(OData);
        Services.AddSingleton(Substitute.For<ICoworkeeApi>());
        Services.AddSingleton(Substitute.For<Realtime.IRealtimeConnection>());
        Services.AddScoped<Realtime.RealtimeClient>();
        Services.AddScoped<Security.PermissionStore>();
        Services.AddScoped<FileDownloader>();
        Services.AddScoped<ReportTheme>();
        Services.AddSingleton(new ReportingOptions());
        Services.AddSingleton(new CoworkeeClientOptions());
        var texts = Substitute.For<Localization.ILocalizationApi>();
        texts.GetTextsAsync(default!, default).ReturnsForAnyArgs(call => new Contracts.Localization.TextsDto(call.Arg<string>(), new Dictionary<string, string>()));
        Services.AddSingleton(texts);
        Services.AddSingleton<Localization.CoworkeeLocalizer>();
        Reports.GetSourcesAsync(default).ReturnsForAnyArgs([Orders]);
        OData.QueryAsync<System.Text.Json.JsonElement>(default!, default!, default).ReturnsForAnyArgs(new ODataPage<System.Text.Json.JsonElement>([], 0, []));
    }

    protected BunitJSModuleInterop Grid { get; }

    protected IReportsApi Reports { get; } = Substitute.For<IReportsApi>();

    protected IODataClient OData { get; } = Substitute.For<IODataClient>();

    protected ReportState State(params ReportWidget[] widgets) => new(new ReportDefinition { Widgets = [.. widgets] }, [Orders]);

    protected static ReportQueryResult Rows(params (string? Key, string? Series, double Value)[] rows) =>
        new([.. rows.Select(r => new ReportQueryRow(
            r.Key is null ? null : new ReportKey(r.Key, r.Key, $"Region eq '{r.Key}'"),
            r.Series is null ? null : new ReportKey(r.Series, r.Series, $"Status eq Shop.Status'{r.Series}'"),
            [r.Value]))], 3);
}
