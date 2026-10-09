# Packages and namespaces

Which package goes where, and what to put in the `using` block.

| Package | Use in | Key namespaces | Main types |
|---|---|---|---|
| `Coworkee.Core` | everywhere | `Coworkee.Core.Modularity`, `Coworkee.Core.Results`, `Coworkee.Core.Security` | `CoworkeeModule`, `DependsOn`, `Result`, `Error`, `ICurrentUser`, `CurrentUserScope` |
| `Coworkee.Domain` | domain | `Coworkee.Domain` | `Entity`, `AuditedEntity`, `AggregateRoot`, `IMultiTenant`, `ISoftDelete`, `RealtimeAttribute` |
| `Coworkee.Contracts` | server and client | `Coworkee.Contracts.*`, `Coworkee.Contracts.Configuration` | DTOs, permission names, `PagedResult`, options classes |
| `Coworkee.Application` | modules | `Coworkee.Application.Messaging`, `.Authorization`, `.Caching`, `.Setup` | `IDispatcher`, `ICommand`, `IQuery`, `IHandler`, `RequiresPermission`, `IPermissionChecker`, `ICachedQuery`, `ISetupStep` |
| `Coworkee.Infrastructure` | infrastructure | `Coworkee.Infrastructure.Persistence`, `.Outbox` | `CoworkeeDbContext`, `IModelContributor`, `AddCoworkeeDbContext`, `AddCoworkeeOutboxProcessing` |
| `Coworkee.AspNetCore` | web hosts | `Coworkee.AspNetCore`, `.Http`, `.Authentication` | `AddCoworkee`, `UseCoworkee`, `IWebModule`, `MapCoworkeeApi`, `ToHttpResult`, `AddCoworkeeApiAuthentication` |
| `Coworkee.OData` | API | `Coworkee.OData` | `CoworkeeODataModule`, `AddODataEntity`, `AddODataImport`, `IODataEntityFilter<T>` |
| `Coworkee.ResponseFilters` | API | `Coworkee.ResponseFilters` | `AddCoworkeeResponseFilters`, `UnlessGranted`, `WhenGranted` |
| `Coworkee.Identity` | API, auth, migrations | `Coworkee.Identity`, `.Setup`, `.Domain` | `CoworkeeIdentityModule`, `AddCoworkeeIdentitySeed`, `SeedRole`, `SeedUser` |
| `Coworkee.AuthServer`, `Coworkee.Account` | auth host | `Coworkee.AuthServer` | `CoworkeeAuthServerModule`, external providers |
| `Coworkee.Bff` | web host | `Coworkee.Bff` | `AddCoworkeeBff`, `MapCoworkeeBff` |
| `Coworkee.Settings` | API | `Coworkee.Settings` | `ISettingDefinitionContributor`, `ISettingProvider`, `AddCoworkeeAppConfiguration` |
| `Coworkee.Mailing` | API | `Coworkee.Mailing` | `IMailTemplateContributor`, `IMailSender` |
| `Coworkee.BackgroundJobs` | API, workers | `Coworkee.BackgroundJobs` | `IBackgroundJob<T>`, `IBackgroundJobs`, `IRecurringJob`, `AddRecurringJob` |
| `Coworkee.Storage` | API, workers | `Coworkee.Storage` | `IBlobStorage`, `BlobLinks` |
| `Coworkee.Search`, `Coworkee.Search.Elasticsearch` | API, workers | `Coworkee.Search` | `ISearchIndex`, `SearchSchema`, `SearchQuery` |
| `Coworkee.Realtime`, `Coworkee.Notifications` | API, workers | `Coworkee.Realtime`, `Coworkee.Notifications` | `IRealtimePublisher`, `INotifier` |
| `Coworkee.Auditing` | API | `Coworkee.Auditing` | audit log queries |
| `Coworkee.Theming` | API | `Coworkee.Theming` | theme store, built-in themes |
| `Coworkee.Ai` | API | `Coworkee.Ai` | `AddAiTool<TRequest>`, MCP server |
| `Coworkee.Localization` | API | `Coworkee.Localization.Resources` | `CoworkeeLocalizationModule`, `ILocalizationResourceContributor`, `AddEmbeddedJson` |
| `Coworkee.Backup` | API | `Coworkee.Backup` | `CoworkeeBackupModule` |
| `Coworkee.Chat` | API | `Coworkee.Chat` | `CoworkeeChatModule`, `ChatEvents` |
| `Coworkee.ExtendedAttributes` | API | `Coworkee.ExtendedAttributes` | `CoworkeeExtendedAttributesModule`, `AddExtendedAttributes<T>` |
| `Coworkee.Social` | API | `Coworkee.Social` | `CoworkeeSocialModule`, `AddCoworkeeSocial` |
| `Coworkee.Client` | SDKs, other .NET apps | `Coworkee.Client` | `CoworkeeApiClient`, `CoworkeeApiException`, `BearerTokenHandler`, `ODataResult<T>` |
| `Coworkee.Client.Blazor` | WebAssembly client | `Coworkee.Client.Blazor`, `.Components`, `.Components.Data`, `.Data`, `.Api`, `.Navigation`, `.Security`, `.Customization` | `AddCoworkeeClient`, `CoworkeeLayout`, `CoworkeeDataTable<T>`, `IODataClient`, `ApiClientBase`, `ReplaceComponent` |
| `Coworkee.Aspire` | app host | `Aspire.Hosting`, `Coworkee.Aspire.Settings` | `AddCoworkeeApp`, `CoworkeeApp`, `WithSetting` |
| `Coworkee.Testing` | tests | `Coworkee.Testing` | `PostgresFixture`, `PostgresWebApplicationFactory`, `AddTestAuthentication` |

## A usual set of usings

```csharp title="A handler"
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
```

```csharp title="A module"
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
```

```razor title="_Imports.razor of a client"
@using MudBlazor
@using Coworkee.Client.Blazor
@using Coworkee.Client.Blazor.Components
@using Coworkee.Client.Blazor.Components.Data
@using Coworkee.Client.Blazor.Security
```
