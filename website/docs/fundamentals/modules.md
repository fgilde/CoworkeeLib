# Modules

A module is a class that registers services and, in web hosts, maps endpoints. Modules declare what they need with `[DependsOn]`; Coworkee loads the whole graph once, in dependency order.

```csharp
[DependsOn(typeof(CoworkeeODataModule), typeof(CoworkeeStorageModule))]
public sealed class MyAppDocumentsModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(MyAppDocumentsModule).Assembly);
        context.Services.AddODataEntity<Document>("Documents", DocumentPermissions.Documents.View, d => d.BlobKey);
    }

    public void ConfigureApplication(WebApplication app) => app.MapDocumentEndpoints();
}
```

| Member | When it runs |
|---|---|
| `ConfigureServices(ModuleServiceContext)` | While the host builds its service collection. `context.Configuration` is the host configuration. |
| `IWebModule.ConfigureApplication(WebApplication)` | After the app is built, in module order. Map endpoints, add middleware. |

## One root per host

A host names one root module, the rest follows from the dependencies:

```csharp
builder.AddCoworkee<MyAppApiModule>();   // registers every module in the graph
var app = builder.Build();
app.UseCoworkee();                       // authentication, problem details, then each IWebModule
```

Hosts without HTTP, such as the migration service, call `services.AddCoworkeeModules<TRoot>(configuration)` instead.

Different hosts compose different graphs from the same modules. In the template the API root depends on the catalog and documents modules, the auth host only on infrastructure, `CoworkeeAuthServerModule` and, with samples, the slim `MyAppRegistrationDocumentsModule` that stores registration documents without the documents API. A module that is not in a host's graph costs that host nothing.

## Contributors instead of central lists

Modules never edit a central list. They add contributors that the owning package collects:

| Contributor | Package | Adds |
|---|---|---|
| `IModelContributor` | Infrastructure | entity mappings to the app's `DbContext` |
| `IPermissionDefinitionContributor` | Application | permission groups and names |
| `ISettingDefinitionContributor` | Settings | runtime settings with scope and type |
| `IMailTemplateContributor` | Mailing | mail templates with defaults per culture |
| `INavigationContributor` | Client.Blazor | menu entries |
| `IAppBarContributor` | Client.Blazor | components in the app bar |

That is why a module can be added or removed without touching anything else.

## Usings

Most code needs few namespaces:

```csharp
using Coworkee.Core.Modularity;          // CoworkeeModule, DependsOn, ModuleServiceContext
using Coworkee.AspNetCore;               // IWebModule, AddCoworkee, UseCoworkee
using Coworkee.Application.Messaging;    // AddMessagingFromAssembly, IDispatcher, ICommand, IQuery, IHandler
using Coworkee.Core.Results;             // Result, Error
```

[Packages and namespaces](../reference/packages.md) has the full list.
