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
});
#if (samples)
builder.Services.AddHttpClient<ICatalogApi, CatalogApi>(client => client.BaseAddress = baseAddress);
builder.Services.AddHttpClient<IDocumentsApi, DocumentsApi>(client =>
{
    client.BaseAddress = baseAddress;
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddSingleton<INavigationContributor, MyAppNavigation>();
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
