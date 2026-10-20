using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Pages;
#if (clientEntities)
using Coworkee.Client.Blazor.ClientEntities;
#endif
#if (reports)
using Coworkee.Client.Blazor.Reporting;
#endif
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
#if (samples)
using Coworkee.Client.Blazor.CommandPalette;
using MyApp.Contracts.Catalog;
using MyApp.Contracts.Documents;
using MyApp.Web.Client.Api;
using MyApp.Web.Client.Pages.Documents;
#endif
using MyApp.Web.Client.Navigation;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.AppTitle = "COWORKEE_APP_TITLE";
    options.AppLogo = "coworkee-icon.svg";
#if (samples)
    options.AssistantHint = "Ask for something, for example \"Which products of Acme cost more than 10?\" or \"Create a brand Northwind with 19% tax\".";
    options.AssistantLinks["add_edit_product"] = "/catalog/products";
    options.AssistantLinks["add_edit_brand"] = "/catalog/brands";
#endif
    options.AppDescription = "Clean Architecture starter for Blazor, built on Coworkee modules.";
    options.AboutLinks.Add(new AboutLink("Project page", "https://github.com/fgilde/CoworkeeLib"));
    options.AboutLinks.Add(new AboutLink("Documentation", "https://fgilde.github.io/CoworkeeLib/"));
#if (airworks)
    options.About.Credits.Add(Coworkee.Client.Blazor.Components.About.AboutCredit.AirWorks("0.6.0"));
#endif
});
#if (samples)
builder.Services.AddHttpClient<ICatalogApi, CatalogApi>(client => client.BaseAddress = baseAddress);
builder.Services.AddHttpClient<IDocumentsApi, DocumentsApi>(client =>
{
    client.BaseAddress = baseAddress;
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddSingleton<INavigationContributor, MyAppNavigation>();
// Ctrl+K: "New product" opens the create dialog; records come from the global search of the server (AddGlobalSearch in the modules)
builder.Services.AddCommandPaletteCommand(new CommandPaletteCommand("New product", MudBlazor.Icons.Material.Outlined.AddBox, CatalogPermissions.Products.Create,
    Href: "/catalog/products?new=true", Keywords: "create add product"));
builder.Services.AddSingleton(new ProfileTab("Documents", "documents", typeof(MyDocuments), DocumentPermissions.Documents.View, MudBlazor.Icons.Material.Outlined.Description));
#endif
builder.Services.Configure<NavigationMenuOptions>(MyAppNavigation.Order);
#if (clientEntities)
builder.Services.AddCoworkeeClientEntities();
#endif
#if (reports)
builder.Services.AddCoworkeeReporting(baseAddress);
#endif
var host = builder.Build();
await host.Services.InitializeCoworkeeClientAsync();
await host.RunAsync();
