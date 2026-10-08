# Pakete und Namespaces

Welches Paket wohin gehört und was in den `using`-Block kommt.

| Paket | Einsatz in | Wichtige Namespaces | Wichtige Typen |
|---|---|---|---|
| `Coworkee.Core` | überall | `Coworkee.Core.Modularity`, `Coworkee.Core.Results`, `Coworkee.Core.Security` | `CoworkeeModule`, `DependsOn`, `Result`, `Error`, `ICurrentUser`, `CurrentUserScope` |
| `Coworkee.Domain` | Domäne | `Coworkee.Domain` | `Entity`, `AuditedEntity`, `AggregateRoot`, `IMultiTenant`, `ISoftDelete`, `RealtimeAttribute` |
| `Coworkee.Contracts` | Server und Client | `Coworkee.Contracts.*`, `Coworkee.Contracts.Configuration` | DTOs, Berechtigungsnamen, `PagedResult`, Options-Klassen |
| `Coworkee.Application` | Module | `Coworkee.Application.Messaging`, `.Authorization`, `.Caching`, `.Setup` | `IDispatcher`, `ICommand`, `IQuery`, `IHandler`, `RequiresPermission`, `IPermissionChecker`, `ICachedQuery`, `ISetupStep` |
| `Coworkee.Infrastructure` | Infrastruktur | `Coworkee.Infrastructure.Persistence`, `.Outbox` | `CoworkeeDbContext`, `IModelContributor`, `AddCoworkeeDbContext`, `AddCoworkeeOutboxProcessing` |
| `Coworkee.AspNetCore` | Web-Hosts | `Coworkee.AspNetCore`, `.Http`, `.Authentication` | `AddCoworkee`, `UseCoworkee`, `IWebModule`, `MapCoworkeeApi`, `ToHttpResult`, `AddCoworkeeApiAuthentication` |
| `Coworkee.OData` | API | `Coworkee.OData` | `CoworkeeODataModule`, `AddODataEntity`, `AddODataImport`, `IODataEntityFilter<T>` |
| `Coworkee.ResponseFilters` | API | `Coworkee.ResponseFilters` | `AddCoworkeeResponseFilters`, `UnlessGranted`, `WhenGranted` |
| `Coworkee.Identity` | API, Auth, Migrationen | `Coworkee.Identity`, `.Setup`, `.Domain` | `CoworkeeIdentityModule`, `AddCoworkeeIdentitySeed`, `SeedRole`, `SeedUser` |
| `Coworkee.AuthServer`, `Coworkee.Account` | Auth-Host | `Coworkee.AuthServer` | `CoworkeeAuthServerModule`, externe Anbieter |
| `Coworkee.Bff` | Web-Host | `Coworkee.Bff` | `AddCoworkeeBff`, `MapCoworkeeBff` |
| `Coworkee.Settings` | API | `Coworkee.Settings` | `ISettingDefinitionContributor`, `ISettingProvider`, `AddCoworkeeAppConfiguration` |
| `Coworkee.Mailing` | API | `Coworkee.Mailing` | `IMailTemplateContributor`, `IMailSender` |
| `Coworkee.BackgroundJobs` | API, Worker | `Coworkee.BackgroundJobs` | `IBackgroundJob<T>`, `IBackgroundJobs`, `IRecurringJob`, `AddRecurringJob` |
| `Coworkee.Storage` | API, Worker | `Coworkee.Storage` | `IBlobStorage`, `BlobLinks` |
| `Coworkee.Search`, `Coworkee.Search.Elasticsearch` | API, Worker | `Coworkee.Search` | `ISearchIndex`, `SearchSchema`, `SearchQuery` |
| `Coworkee.Realtime`, `Coworkee.Notifications` | API, Worker | `Coworkee.Realtime`, `Coworkee.Notifications` | `IRealtimePublisher`, `INotifier` |
| `Coworkee.Auditing` | API | `Coworkee.Auditing` | Abfragen des Audit-Logs |
| `Coworkee.Theming` | API | `Coworkee.Theming` | Theme-Speicher, eingebaute Themes |
| `Coworkee.Ai` | API | `Coworkee.Ai` | `AddAiTool<TRequest>`, MCP-Server |
| `Coworkee.Localization` | API | `Coworkee.Localization.Resources` | `CoworkeeLocalizationModule`, `ILocalizationResourceContributor`, `AddEmbeddedJson` |
| `Coworkee.Backup` | API | `Coworkee.Backup` | `CoworkeeBackupModule` |
| `Coworkee.Chat` | API | `Coworkee.Chat` | `CoworkeeChatModule`, `ChatEvents` |
| `Coworkee.ExtendedAttributes` | API | `Coworkee.ExtendedAttributes` | `CoworkeeExtendedAttributesModule`, `AddExtendedAttributes<T>` |
| `Coworkee.Client` | SDKs, other .NET apps | `Coworkee.Client` | `CoworkeeApiClient`, `CoworkeeApiException`, `BearerTokenHandler`, `ODataResult<T>` |
| `Coworkee.Client.Blazor` | WebAssembly-Client | `Coworkee.Client.Blazor`, `.Components`, `.Components.Data`, `.Data`, `.Api`, `.Navigation`, `.Security`, `.Customization` | `AddCoworkeeClient`, `CoworkeeLayout`, `CoworkeeDataTable<T>`, `IODataClient`, `ApiClientBase`, `ReplaceComponent` |
| `Coworkee.Aspire` | AppHost | `Aspire.Hosting`, `Coworkee.Aspire.Settings` | `AddCoworkeeApp`, `CoworkeeApp`, `WithSetting` |
| `Coworkee.Testing` | Tests | `Coworkee.Testing` | `PostgresFixture`, `PostgresWebApplicationFactory`, `AddTestAuthentication` |

## Übliche Usings

```csharp title="Ein Handler"
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
```

```csharp title="Ein Modul"
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
```

```razor title="_Imports.razor eines Clients"
@using MudBlazor
@using Coworkee.Client.Blazor
@using Coworkee.Client.Blazor.Components
@using Coworkee.Client.Blazor.Components.Data
@using Coworkee.Client.Blazor.Security
```
