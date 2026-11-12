# Changelog

Changes that need action in apps built on Coworkee. Newest first.

## Unreleased

### Added

- AI assistant: reads of data sets the browser holds as [client entities](../ui/client-entities.md) are answered from the local copy, in milliseconds instead of a database query. A `query_data` on a set the window names in `AiUiContext.LocalSets` streams a `client_query` event; the window answers it from the copy and posts the rows (the server tool's shape) to `POST /api/v1/ai/sessions/{id}/client-results`. The server runs the read itself when the copy cannot answer it or no answer comes within `Ai.LocalQueryTimeout` seconds (4 by default); `Ai.LocalQueries` = `false` switches it off. The audit marks these calls with the channel `chat-local`. Only the user of the running turn can answer, and only `query_data` goes to the browser; `global_search`, writes and MCP stay on the server. Apps change nothing. With several API instances route a user's requests to the same instance (sticky sessions), else the server answers after the timeout. See [Answers from the browser's local data](../modules/ai.md#answers-from-the-browsers-local-data).
- `IClientDataCache.LocalSets` and `QueryLocallyAsync(entitySet, query)`: the sets that answer locally right now and a query answered from the local copy as `LocalRows` (rows by field name and the total), or `null` when only the server can. `IAssistantApi` has `SendClientResultAsync`; an own implementation of `IAssistantApi` adds it.

## 0.0.1.81 (2026-11-12)

### Added

- Facets without attributes: OData facets, their counts and report labels read facets through Nextended's `FacetRegistry`, so facets configured in code (fluent `Facets<T>(...)` in a `CodeGenConfig` class, or `FacetRegistry.Register`) count like `[ProvideFacet]`. Entities with attributes only work as before. See [Facets without attributes](../web/odata.md#facets-without-attributes) and [Configuring in code](../fundamentals/dtos.md#configuring-in-code-fluent).
- Gravatar for user avatars: `UserAvatar` (and with it `UserCard`, the account menu, chat, comments, workflows, logs, profile and person pages) shows a user's Gravatar while they have no uploaded picture, and the initials when gravatar.com has none (`d=404`). An uploaded picture never asks gravatar.com. `UserCardDto` has the new optional `GravatarHash` (SHA-256 of the trimmed, lower-case email address; null with a picture). New setting `Theme.Gravatar` (*Gravatar pictures*, system, organisation or user, default `true`): `false` shows the initials only and no browser contacts gravatar.com. Privacy: with the setting on, gravatar.com receives the email hash and the viewer's IP address; apps that must not do that set `Theme.Gravatar` to `false` (for example `seed.Settings["Theme.Gravatar"] = "false"`). See [People](../ui/client.md#people).

### Changed

- Nextended 10.1.61 (`Coworkee.CodeGen` brings Nextended.CodeGen 10.1.61: fluent facets, `ViewDefaults`, `OrderLast`, `DeleteStaleFiles`). Apps that pin `Nextended.Core` below 10.1.61 fail the restore with NU1605: `coworkee update` raises the pinned Nextended packages.

### Fixed

- App bar at the left or right edge: the bar scrolled sideways. `overflow-y: auto` alone turns `overflow-x` to `auto`, and the account button kept its `ml-2` (an `!important` spacing class the side bar's `margin: 0` lost to), so it stood 8 px past the 64 px bar; a window too short for the bar added its vertical scrollbar to that. The side bar now hides horizontal overflow, uses a thin scrollbar, drops the margins of its buttons and leaves the name and the impersonation chip room for the scrollbar; the account button's drop-down arrow is hidden there as intended (the selector named a class MudBlazor does not use). Bars at the top and the bottom are unchanged.

## 0.0.1.80 (2026-11-12)

### Changed

- Nextended 10.1.59 (`Coworkee.CodeGen` brings Nextended.CodeGen 10.1.59): the generator keeps no state between compilations, so modules with a `codegen.json` built in parallel by one compiler server (Visual Studio, `dotnet build` of the solution) no longer write DTOs into each other's folders. `Coworkee.CodeGen` no longer sets `UseSharedCompilation` to `false`; those projects compile in the shared compiler server again. `coworkee update` moves the pinned Nextended packages (`Nextended.Core`, `Nextended.Web`, `Nextended.CodeGen`, `Nextended.Aspire.Hosting.*`). Nextended changes are additive: an app may pin any newer Nextended version, but none older than the one Coworkee references (NU1605).

## 0.0.1.79 (2026-11-11)

### Fixed

- Command palette: its backdrop lay over the palette, so results could not be clicked. The app bar is a stacking context (fixed with a z-index) and held the palette, whose dialog had no z-index of its own below the backdrop's 1450. The palette now sits beside the app bar (the bar shows only the search button, `CommandPaletteButton`) and its dialog lies above its backdrop, in every header position, in the second bar, in the overflow menu and hidden. Apps that place `<CoworkeeCommandPalette />` in a bar of their own should place it outside that bar with `ShowButton="false"` and open it from `<CommandPaletteButton OnClick="..." />`.

## 0.0.1.78 (2026-11-11)

### Fixed

- `CoworkeeDataTable`: a `PropertyColumn` through two or more navigations (`o => o.Supplier.Organization.Name`) threw a `NullReferenceException` in every row where a navigation before the last was null, also with a `CellTemplate` or `Editable="false"` (MudBlazor 9.12 guards only the last step of the cell expression and the grid reads the value for editing in any case). Every dotted `PropertyColumn` gets a null safe cell, sort and group function at any depth, also in the rows of open groups; the OData paths stay the same. See [Data tables](../ui/data-table.md#columns-grouping-and-views).

### Changed

- Security updates: ASP.NET Core, EF Core and Microsoft.Extensions packages 10.0.15 (`Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.14 has the high-severity advisory GHSA-wr8v-9x5q-ggcm), Aspire 13.6.5 (app host SDK, `Aspire.Hosting.*`, `Aspire.Hosting.Testing`, `Aspire.StackExchange.Redis`; 13.6.4 has GHSA-f7vh-h5g3-7jrh; `Coworkee.Aspire` references `Aspire.Hosting.AppHost` 13.6.5 itself, as the Nextended Aspire packages bring 13.6.4). New apps get them; `coworkee update` moves the pinned Aspire packages and the app host SDK. Apps: raise their 10.0.14 references to 10.0.15 too, otherwise the restore fails with NU1605.
- Nextended 10.1.55 (`Coworkee.CodeGen` brings Nextended.CodeGen 10.1.55): a second `[AutoGenerateDto]` on an entity generates one more DTO, a view; an entity with one attribute generates the same as before. See [DTOs and mapping](../fundamentals/dtos.md#mark-the-entities).

## 0.0.1.77 (2026-11-11)

### Fixed

- Enums over the wire: OData and the API send enum names, but `IODataClient`, `ApiClientBase` (and so the application service proxies), client entities and `Coworkee.Client` read with web defaults only, so a DTO with an enum failed on `"Status":"Shipped"`. They all use `CoworkeeJson.Web` (`Coworkee.Contracts`) now: names and numbers read, `[Flags]` round-trip as `"A, B"`, request bodies carry names (the API's minimal APIs accept names and numbers; an own MVC controller needs a `JsonStringEnumConverter` in its `JsonOptions`). `Coworkee.Client` references `Coworkee.Contracts`. See [OData from the Blazor client](../web/odata.md#from-the-blazor-client).
- `CoworkeeDataTable`: a `PropertyColumn` through a navigation (`o => o.OrderStatus.Code`) threw a `NullReferenceException` where the navigation was null when the grid grouped the loaded page (data source, client without `GroupAsync`) or sorted its items, and a value type behind it showed `0`. Such columns get null safe sort and group functions (unless the page sets `SortBy`/`GroupBy`), the cell stays empty, the OData paths stay the same. `None` stand-in objects in DTOs can go. See [Data tables](../ui/data-table.md#columns-grouping-and-views).

## 0.0.1.76 (2026-11-10)

### Changed

- User administration: the *Roles* tab is one filterable checklist of all roles, the user's roles ticked on top, with a count (*3 of 12 assigned*); the *Groups* tab shows *Member of* on top and the other groups below as a virtualized checklist (lazy tree without filter text, server search matches by path with it). *Add role*, *Add group* and their pickers are gone, and the dialogs of the users list (*Roles of Bob* …) are usable at once. A clicked row keeps its place until the filter changes, the tab opens again or a realtime reload comes after three seconds without a click. All three tabs say *Read only* to who may not change them. E2E tests that clicked `user-roles-add`/`user-roles-picker` tick the role in `user-roles-list` instead; the rows carry `data-role`/`data-group` with the id. See [User administration](../features/identity.md#user-administration).

### Fixed

- Azure Container Apps: SQL Server ran with the default 0.5 CPU and 1 GiB, ran out of memory under load ("insufficient system memory in resource pool 'internal'"), refused connections in the pre-login handshake, and every sign-in and page load failed. The SQL Server container gets 1 CPU and 2 GiB now; deploy again.
- Permissions in the browser: a server that did not answer (5xx, no connection) counted as "no permissions": the navigation emptied and pages said *You lack the permission for this page* although the user was signed in. Only a refusal (401, 403) means no permissions now; otherwise the last known permissions stay, `PermissionStore.Unavailable` is set, the page shows *The server does not answer right now. Trying again…* and the store asks again after a few seconds.
- Realtime: behind several instances (Container Apps scales out) the negotiate request and the WebSocket after it landed on different instances; the second answered 404 and the connection stood only after a few attempts. The client opens the WebSocket directly (no negotiate) and falls back to the negotiated connection where WebSockets do not get through.

## 0.0.1.75 (2026-11-10)

### Added

- CLI: `--codegen` for `coworkee module add`, `coworkee entity add` and `coworkee entity import` (MCP tools `add_module`, `add_entity`, `import_entities`: `codegen`). Instead of writing `<Entity>Dto` and its mapping once, the CLI marks the entity with `[AutoGenerateDto]`, references `Coworkee.CodeGen` (and `Riok.Mapperly`) in the module, creates a `codegen.json` next to it when there is none and builds the module once, so DTO and mapping are generated on every build and the next build compiles. Mapperly is the default mapping, `--mapping Manual` takes the generated extension methods. References must point to generated entities of the same module; inverse collections stay out of the DTO. See [DTOs and mapping](../fundamentals/dtos.md#with-the-cli) and [CLI](../getting-started/cli.md#generated-dtos).

### Fixed

- `Coworkee.CodeGen`: projects with a `codegen.json` compile in a compiler process of their own (`UseSharedCompilation` `false`). Nextended.CodeGen shares its generator between the compilations of one compiler server, so two modules with generated DTOs built in parallel wrote DTOs into each other's folders and broke the contracts build. Apps that worked around it can drop their own setting.

## 0.0.1.74 (2026-11-10)

### Added

- User page (*Administration › Users › a user*): tabs *General*, *Roles*, *Groups*, *Permissions* and *Sessions*. Roles and groups are filterable, virtualized tables; *Add role* opens a filterable list, *Add group* the lazy group tree with its search (no more list of the first 200 groups, so organisations with thousands of groups work). Every tick and every remove saves on its own at once: the change shows before the server answers, saves run one after another in the background without locking the page, and a failed one takes back only its item and says why in a snackbar. The tabs reload when another administrator changes roles, groups or grants (`RealtimeTopics.TenantPermissions`). *Permissions* lists what the user holds with its source (role, role through a group, group, granted directly, computed) and grants permissions to him directly (`Identity.Permissions.Manage`). The tabs are components of their own (`UserRolesTab`, `UserGroupsTab`, `UserPermissionsTab` in `Coworkee.Client.Blazor.Components.Users`, parameter `UserId`) ([user administration](../features/identity.md#user-administration)).
- Users list: an action menu per row with *Roles…*, *Groups…*, *Permissions…* and *Sessions…* (the same components in a side sheet), *Send password reset*, *Sign out everywhere*, *Lock*/*Unlock*, *Log in as* and *Delete user*, each with its permission. `UserRow` has the new optional member `LockoutEnd`.
- Single changes instead of whole sets, so two administrators editing the same user do not undo each other: `POST`/`DELETE /api/v1/identity/users/{id}/roles/{roleId}` (`AddUserRole`, `RemoveUserRole`, `Identity.Users.Manage`), `POST`/`DELETE /api/v1/identity/permissions/grants/{providerType}/{providerKey}/{name}` (`AddGrant`, `RemoveGrant`, `Identity.Permissions.Manage`); memberships use the existing `groups/{id}/members/add` and `remove`. `GET /api/v1/identity/users/{id}/permissions/sources` (`GetUserPermissions`, `Identity.Users.View`) returns `UserPermissionsDto`. `ICoworkeeApi` has the new members `AddUserRoleAsync`, `RemoveUserRoleAsync`, `AddGrantAsync`, `RemoveGrantAsync` and `GetUserPermissionsAsync`: hand-written implementations and test fakes add them.
- Coworkee Studio, deploy wizard: the subscriptions of every account the Azure CLI keeps signed in (`az account list --all`), grouped by account and tenant; the account follows the picked subscription. *Sign in with another account* runs `az login`, with an optional tenant (`--tenant`, for guest access), and keeps the other accounts signed in. An update preselects the subscription of its record; when no signed-in account sees it, the wizard says so and prefills the record's tenant for the sign-in. See [Studio](../getting-started/studio.md#deploy).
- Own Azure sign-in per app (off by default): the Studio switch *Own Azure sign-in for this app* and `coworkee deploy azure --apply --own-azure-sign-in` give the app its own Azure CLI folder (`AZURE_CONFIG_DIR` = `~/.coworkee/azure/<app>-<hash>`, never in the repository) for every `az` call, the deploy and the app host; the global sign-in stays untouched. The record keeps `ownAzureSignIn`, so updates use the same sign-in; `--own-azure-sign-in false` turns it off. See [Deployment](../hosting/deployment.md#deploying-from-your-machine).
- Theming: `ShowAppNameInAppBar` and `ShowAppNameInNav` decide whether the app's name shows next to the logo in the app bar and at the head of the navigation (both on by default, as before).
- One declaration of a set's search fields: `AddODataEntity<T>(set, permission, o => o.SearchFields(p => p.Name, p => p.Brand!.Name))` serves `$search`, the search box of every `CoworkeeDataTable` of the set without own `SearchFields` (the table asks `GET /api/v1/search/fields` once per session) and `AddGlobalSearch<T>`, which no longer needs `Fields` when the set declares them. `Searchable="false"` hides the box ([search fields](../ui/data-table.md#search-fields)).
- Command palette: records of a [client entity](../ui/client-entities.md) whose copy is in the browser show at once from the copy, before the server answers; the server's hits of the same records join without doubles. `GET /api/v1/search/providers` tells the client how (`GlobalSearchProviderDto.Local`); `IGlobalSearchProvider.Local` and `IGlobalSearchApi.GetSearchFieldsAsync` are new members with defaults ([local copies first](../web/global-search.md#local-copies-first)).
- Command palette: **Ctrl+Enter** (**Cmd+Enter**) opens all results (`/search?q=`); the footer shows it ([command palette](../ui/command-palette.md)).

### Changed

- Users list: the roles column only shows the roles; they change through *Roles…* of the row's menu, one role at a time. The inline effective-permissions panel and the icons of the row are gone (*Permissions…* and the menu replace them). `GroupTree` takes `NodeChecked` to report one checkbox click at a time.
- Command palette: **Enter** right after typing opens the chosen entry only when its title is exactly what was typed (a full number, a command's name); otherwise, also while the search runs or without hits, it opens all results. After choosing with the arrows or the mouse it opens the entry as before.
- Data tables over a local copy wait 90 ms after the last key before searching instead of 300 ms (the server keeps 300 ms).

### Fixed

- Azure deployment with several signed-in accounts: Aspire's Azure CLI credential asked `az` for a token of the tenant only and got the account of the CLI's default subscription, which may not see the target subscription. With `Azure:CredentialSource=AzureCli` and `Azure:SubscriptionId` (as `coworkee deploy azure --apply` sets them), `AddCoworkeeApp` asks for the token of the subscription. Update the Coworkee packages of the app host.
- Group picker: an administrator adding or removing a membership (or moving or deleting a group) reached the member only after a reload; the picker kept the lost group and the browser copies (client entities) kept its rows. `GroupScopeState` now follows `RealtimeTopics.TenantPermissions`, reloads the user's groups once per burst and, when they or the selection changed, drops the copies of the old scope and raises `Changed`, so tables reload at once.
- Azure Container Apps: the *Services* page of a deployment listed nothing. The tools' public addresses were left out of `Coworkee:Services` on Azure; they are written again. Deploy again.
- Layout: with the app bar at the left or right edge alone and the navigation unpinned (overlay), the navigation could not be opened any more: its only menu button sat in the hidden navigation. The side bar shows a menu button then.
- Theming: the theme's logo was squeezed to the width of its place; it keeps its proportions now.
- Client entities in SQLite: every search of a data table went to the server. The filter's `tolower` became `ToLowerInvariant`, which EF Core does not translate; it is SQL's `lower()` now. Filters on nested objects (JSON columns) or computed properties, and orders by them, run on the copy's rows in memory instead of the server.

## 0.0.1.73 (2026-11-10)

### Fixed

- Sessions page (*Administration › Monitoring › Sessions*): the list of active sessions never loaded, the bar kept running, and after *Show ended* and back the rows of the ended mode stayed. `UserSession.EndedAt` and `EndReason` had no public setter, so the OData model left them out and the server refused the page's filter `EndedAt eq null` (400); the ended list showed no end and no reason. Both are in the model now.
- Mail log (*Administration › Communication › Mail log*): the search, the campaign filter and sorting by date were refused (400) and the rows came without recipient, subject, template and date: `OutgoingMail.To`, `Subject`, `TemplateName`, `CampaignId` and `QueuedAt` had no public setter and were missing from the OData set `Mails`. They are in it now.
- Data tables: a query the server refuses shows *The data could not be loaded.* and an empty table instead of a running bar over the rows of the last load.

## 0.0.1.72 (2026-11-10)

### Changed

- Coworkee Studio: the language code in the title bar opens a menu with the language of the system and the eight languages, the current one checked, instead of switching to the next language on every click.

### Fixed

- Azure Container Apps: the Grafana dashboards of a deployment stayed empty because the services could not resolve the collector (`Name or service not known (otel-collector:4318)`): Container Apps gives an app named `otel-collector` no name inside the environment. The collector is named `telemetry` in an Azure deployment; deploy again, then delete the old `otel-collector` container app.
- Coworkee Studio: the logo of the title bar stays hidden while the mark of the splash flies onto it, so only one logo is seen; it appears when the mark lands, also when the animation is skipped.
- Sessions page (*Administration › Monitoring › Sessions*): it loaded again and again. Its filter of active sessions carried the current time, so every render was a new filter and the table reloaded, with the user names and counts, as long as a load took more than a second. The filter is now set when the mode changes. *Show ended* loads once with the filter of the new mode (it also loaded with the filter of the mode just left, and the two loads raced: rows of both modes, columns of one). The page marks the caller's own session (*This session*) and a session that ran out without an end (*Expired*, no actions), so two rows of one user read as two sessions.
- Client entities: the sync ring of the table toolbar showed nothing while a copy in SQLite loaded. SQLite takes the whole copy as one snapshot file, so no rows arrived one by one and the ring by rows stayed at zero. A download with no row in yet shows a running ring, at no cost to the sync; downloads page by page keep their ring by rows.
- Advanced filter and enums: in a SQLite copy, `has` (the rules of a `[Flags]` enum) matched nothing, because the copy keeps enums by name and the bits read 0 from the text; it now compares the names of every value with the flag. The query builder of a server table takes a `[Flags]` enum of the client as a plain enum when the server's facet of the property says so: its rules compare with `eq` instead of `has`, which a server with a plain enum (stored as text) could not answer, and the zero member is a choice again.

## 0.0.1.71 (2026-11-10)

### Fixed

- Coworkee Studio: the tiles of the start page grow with their text instead of cutting it off (*Run, deploy or extend an app you made.*); the deploy card of the create page puts its buttons under the status text, which they squeezed to a few letters per line.

## 0.0.1.70 (2026-11-09)

### Added

- Coworkee Studio speaks French, Spanish, Italian, Dutch, Polish and Portuguese besides English and German: the language box in the settings lists them all, the language code in the title bar switches to the next one, `--lang` takes their codes ([Language and updates](../getting-started/studio.md#language-and-updates)).

### Fixed

- `coworkee deploy azure --apply` hung in the publish step when a parameter had an empty value (SMTP host, port, user and sender without a mail server): Aspire counts an empty value as missing and waits for one silently. The CLI keeps empty values out of Aspire's deployment state and removes those a former deployment left there; the app host starts a parameter with an empty configured value empty too.

## 0.0.1.69 (2026-11-09)

### Added

- `coworkee deploy azure --resource-group <name> --location <region> --apply` deploys to Azure Container Apps into that one resource group (made when missing; deployments scoped to it, nothing at the subscription): it checks the Azure CLI sign-in with a real token, keeps the generated parameters per deployment in `.coworkee/deployments/<name>.secrets.json` (git ignored), lets Aspire build, push and deploy, runs the migrations job and writes `.coworkee/deployments/<name>.json` with the addresses ([Deployment](../hosting/deployment.md)).
- Azure Container Apps, found in the first real deployment and fixed in the library: public, tool and single-page-app addresses come from the apps themselves (no address parameters); parameters without value no longer make the publish wait silently; managed storages get unique names; the services' files volume is one share; secret names lose their dots; containers with state run as one replica; Azure Files mounts skip byte-range locks (SQLite of Grafana and WebDataStudio); Grafana keeps its data under `/var/lib/grafana-data` so its baked dashboards stay visible; the auth server retries seeding its clients until the schema exists.
- Coworkee Studio: an existing app (*Open existing app*, *Recent*, *My apps*) opens its overview instead of the wizard: run status with **Start**/**Stop** and links, its deployments from `.coworkee/deployments` with their addresses, folder, solution and VS Code; **Edit in the wizard** leads to the changes ([Open existing app](../getting-started/studio.md#open-existing-app)). **Deploy…** is a wizard for Azure: Azure CLI installed and signed in (with the fix for the Windows broker problem), subscription, resource group and region, a summary, then `coworkee deploy azure --apply` with its output live; **Update** deploys a recorded deployment again ([Deploy](../getting-started/studio.md#deploy)).
- `coworkee deploy azure --apply`: the deployment record keeps the tool switches (`tools`), so an update deploys the same tools.
- GitHub deploy workflow: `coworkee deploy azure --github` writes `.github/workflows/deploy-azure.yml`, which deploys every push to the listed branches to Azure Container Apps, one GitHub environment and one resource group (`<prefix>-<branch>`) per branch, signed in with OIDC; the generated parameters of an environment stay in its secret `COWORKEE_DEPLOY_SECRETS`, a deleted branch can take its resource group along. The options `deployWorkflow`, `deployResourceGroupPrefix`, `deployLocation`, `deployBranches` and `deployTeardown` write it from `coworkee new`, a spec or `coworkee add`; Coworkee Studio edits the settings in the deploy card (*GitHub deploy workflow…*). A workflow changed by hand is overwritten only when asked to ([Deployment](../hosting/deployment.md#deploying-every-branch-with-github-actions)). The one-time setup in Azure and GitHub is yours.
- Seed contributors: `ISeedContributor` (`Coworkee.Application.Setup`) for data every database needs, in a module or the migrations project with `[RegisterAs(typeof(ISeedContributor))]`. The migrations run them on every run after the identity seed and the development users, by `Order`, for every tenant or once (`Target`), in development runs and/or deployments (`Environments`), each call in its own scope and transaction as the system with every permission; a failing one stops the run with exit code 1 unless it is not `Required`. A tenant set up in the browser or created later gets the per-tenant contributors of the modules from the api right away, after the commit. `RunSeedContributorAsync<T>` runs one in tests ([seed contributors](../fundamentals/persistence.md#seed-contributors)).
- `CoworkeeMigrationsOptions.SyncRolesInDeployments`: the roles of `DeploymentSeed` and their grants reach deployed databases on every run (added, never removed; users untouched). Off by default.
- CLI: `coworkee seed add <Name> [--module <Module>] [--host] [--development | --deployment]` and the MCP tool `add_seed` write a seed contributor ([CLI](../getting-started/cli.md#seed-contributors)).
- Security log: sign-ins (succeeded or failed with the reason), sign-outs, lockouts, changes of passwords, e-mail addresses, two-step verification and passkeys, *Log in as*, API keys (created, revoked, first use of a day) and roles and own permissions of a user, with organisation, user, actor, impersonator, client, IP address, browser, trace id and details. Always on, written by every host with the app database (table `cw.SecurityEvents`); credential and access changes are taken from the saves, so every code path counts. `ISecurityLog.WriteAsync(new SecurityLogEntry(...))` writes entries of your own (`SecurityActions.SessionEnded`, ...). Page *Administration > Monitoring > Security log* (`Audit.SecurityLog`), OData set `SecurityEvents`, a link on the user page ([security log](../modules/auditing.md#security-log)).
- Request log: every API request with user, impersonator, client or API key, method, path, route, dispatched request, status, duration, IP address, browser, trace id, exception, headers and JSON/form bodies (redacted: `Authorization`, cookies, API keys and every name with password, secret, token, apikey or credential, also in query strings and nested bodies; cut at `RequestLog.MaxBodyKb`, 32 KB). Captured without buffering, saved in batches by a background service from a bounded queue that drops (metric `coworkee.request_log.dropped`, meter `Coworkee.Auditing`) instead of slowing requests; about 0.1 ms per request in the tests. The setting `RequestLog.Requests` (Reading, Writing, both by default; nothing chosen turns it off) applies at once on every instance. Page *Administration > Monitoring > Request log* (`Audit.Requests`; bodies need `Audit.Requests.Bodies`), OData set `RequestLog` (without headers and bodies), `GET /api/v1/audit/requests/{id}`, *Open trace* into Grafana or the Aspire dashboard (`GET /api/v1/audit/trace-viewer`), `.SkipRequestLog()` for endpoints ([request log](../modules/auditing.md#request-log)).
- Retention targets `security-events` (365 days) and `request-log` (30 days); personal data sections `securityLog` and `requests`, erasing keeps the entries without the user.
- Settings: the type `MultiChoice` (several of the `choices`, stored comma separated, an empty value means none) in the Blazor settings and the AirWorks settings app.
- Active sessions: every sign-in at the auth server is a session with device, browser, IP address, app and last activity. **Administration › Monitoring › Sessions** lists them live with the number per user and ends one or all of a user, the user page shows the user's, and users end their other sessions on the account page (*Security › Sessions*). Ending one revokes exactly its tokens, refuses its sign-in and signs that browser out at once (`SessionRevoked` with `EndedSessionId`); permissions `Identity.Sessions.View` and `Identity.Sessions.Manage`, requests and endpoints under `/api/v1/identity/sessions` and `/users/{id}/sessions`, the OData set `UserSessions`, `ISessionsApi` in Blazor, page key `sessions`. Each end writes `UserSessionEnded` to the outbox for a security log ([sessions](../security/clients.md#sessions)).
- Session limit: the settings `Account.Sessions.MaxPerUser` (default 0, unlimited) and `Account.Sessions.LimitMode` (`EndOldest`, the default, or `Refuse`) and a limit per user (`User.MaxSessions`, `PUT /api/v1/identity/users/{id}/session-limit`). A sign-in beyond it ends the oldest sessions or is refused ([session limit](../security/clients.md#session-limit)).
- Security log and sessions: every ended session is a `SessionEnded` entry of the security log (the end reason, who ended it, the session id), and a sign-in the session limit refuses is a failed sign-in with the reason `SessionLimit` ([security log](../modules/auditing.md#security-log)).
- Roles page: the pencil of a row and of the role's page renames a role and changes its description; the API could, the page offered no way. *Move or copy users…* gives the users of a role (all, or the chosen ones) other roles and keeps or takes the role (`POST /api/v1/identity/roles/{id}/users/transfer`, `Identity.Users.Manage`, `ICoworkeeApi.TransferRoleUsersAsync`), with the account, grant and last-administrator rules of single changes; `GET /api/v1/identity/users?roleId=` lists the users of a role ([renaming roles and moving their users](../security/permissions.md#renaming-roles-and-moving-their-users)).
- `Coworkee:Identity:FixedRoleNames` (role name: what relies on it): roles code looks up by name are neither renamed nor deleted (`409 identity.role_name_fixed`, the message names the code). Files adds *Registration documents*, Coworkee.Workflows the roles its workflows name. Add the role names your code or data use; check seeds, Elsa *Notify* activities and stored names before renaming or deleting a role.
- Coworkee Studio: a splash right at start (the window shows in about a second instead of 2.5): the animated Coworkee mark, the version and what is loading; once the Studio is built behind it, the mark flies into the title bar. A click or `Esc` skips it, **Reduce motion** in the settings fades instead ([Install and start](../getting-started/studio.md#install-and-start)).

### Changed

- The migrations run drops the cached permissions, settings and features of running services at its end (with Redis on every instance).
- Template: the sample catalog (`DemoData`) is a seed contributor for local runs into a new database; deployments with deploy parameters no longer get sample data. Existing apps keep their `ISetupStep` registration and behave as before. `AfterSeed` stays; its documentation points to seed contributors.
- `CoworkeeAuditingModule` depends on `CoworkeeSettingsModule`. The template adds the meter `Coworkee.Auditing` to OpenTelemetry. Apps need a migration for the tables `cw.SecurityEvents` and `cw.RequestLog`.
- Identity: sessions are tracked in the new table `cw.UserSessions`, and `cw.Users` gets the column `MaxSessions`; apps need a migration. `RealtimeConnections.EndAsync` and `SessionRevokedPayload` take an ended session id (`EndedSessionId`), `IUserSessionListener` gets `SessionEndedAsync` (implemented by default). The auth server names every authorization it creates after its session (`coworkee_sid`), also outside "Log in as".
- `PUT /api/v1/identity/roles/{id}` answers `409 identity.role_system` for a system role instead of 404.
- The users list, the user page and the roles tab of a group show renamed and new roles at once.
- Client: a failed API call shows the message of the problem (`ApiException.Title`) in snackbars and edit dialogs instead of its code, translated like the app's texts (English without a translation). `Error.Conflict(code, template, arguments)` sends a message with variable parts translatable (`template`, `arguments` in the problem) ([server messages](../modules/localization.md#in-the-client)); the role messages ship in all languages.
- Client entities (SQLite): more than 500 changed rows at once (more than ten chunks of 50) come as one snapshot of the set instead of chunks by id, each of which cost the server a read of the visible keys; the table keeps searching the old copy until the file replaces it ([how the copy works](../ui/client-entities.md#how-the-copy-works)). The copies in memory are unchanged.
- Client entities: a refresh keeps the copy again (IndexedDB) and reloads its tables and facet counts only when a row was added, removed or a value changed. Rows the change feed reports again unchanged (within its `Overlap`) no longer write the whole SQLite file or rebuild the copy in memory; real changes appear as fast as before. A custom `IClientEntityStore<T>` returns from `ApplyAsync` whether it changed a row (`Task<bool>`).

### Fixed

- Client entities (SQLite): an update of rows the copy already had, or a delete the change feed reported again (its overlap), no longer reads every visible id on the server and in the browser; the copy compares ids only when its row count after the changes differs from the server's.
- Client entities: realtime nudges that arrived while a sync ran each waited for a sync of their own, so a copy whose sync took longer than the changes came synced back to back; they now bring at most one more sync.
- Client entities: after another group selection the switch of a table kept showing the disposed set as local, and a copy in SQLite kept its database connection open; a disposed set now reports that it searches on the server (the switch follows the set of the new selection) and closes its store.
- Client entities: the menu of the table's switch checked *Search in the browser* whenever the browser was chosen, also while the queries still went to the server (the first download, a restored copy before its first update, a set too large, a failed download, a paused sync); the check now sits where the queries go, and a copy on its way reads *Search in the browser (loading…)* ([the switch](../ui/client-entities.md#the-switch-in-the-table)).

## 0.0.1.68 (2026-11-08)

### Added

- App specs: an entity takes a `workflow` (states, transitions with their permission, approvals with deadline and reminder, notifications, time stamps, a mirrored state). The CLI writes the declaration for Coworkee.Workflows, registers it and adds the state column to the list, in new apps and with `coworkee add --spec` or `coworkee entity add` ([workflows in a spec](../getting-started/cli.md#workflows)); Coworkee Studio edits it on the *Entities* page.
- Starter specs: ERP (order to cash, purchase to pay, invoices), HR self-service (leave and expenses), repair shop and recruiting. The six others got their processes: deals with a discount approval, tickets with escalation, orders through shipping with a refund approval, events on sale and reservations with waitlist and check-in, a rights check for assets, requests with a reminder ([starter specs](../getting-started/gallery.md)).
- Coworkee Studio: *Recent* on the start page lists apps next to specs; an app opens in the wizard to extend it, and *My apps* has **Edit in the Studio**.

### Changed

- App specs: entity names that are namespaces of the app (`Application`, `Contracts`, `Infrastructure`, `Domain`, `Features`, `Persistence`, `Permissions`, `Endpoints`) are refused; the namespace hid the type and the app did not build.

### Fixed

- Template: a new app referenced the Microsoft packages in 10.0.13, which has a known vulnerability (NU1903) and is older than the Coworkee packages need (NU1605), so its restore failed; they are 10.0.14 now.

## 0.0.1.67 (2026-11-07)

### Changed

- Client entities: `AddCoworkeeClientEntitiesSqlite()` keeps only the row types in SQLite that the in-memory copy keeps as JSON (a list among their properties); row types of simple properties stay columns in memory, whose facets answer about ten times faster. `AddCoworkeeClientEntitiesSqlite(o => o.AllRowTypes = true)` puts every copy into SQLite ([which copies](../ui/client-entities.md#which-copies)).

### Fixed

- Client entities (SQLite): range facets over many distinct values brought every value into the browser (100,000 order dates: 1.3 s); they count in one statement now (about 60 ms). The values of a filter are parameters, so another search text or choice no longer compiles every statement again: a filtered count and page took 38 and 36 ms, now 7 and 5 ms.

## 0.0.1.65 (2026-11-07)

### Added

- Client entities: `Coworkee.Client.Blazor.ClientEntities.Sqlite` keeps the local copies in SQLite inside the browser (`AddCoworkeeClientEntitiesSqlite()`, EF Core; the package turns on `WasmBuildNative`, building the client needs the `wasm-tools` workload). The server sends the first copy as one SQLite file (`POST /api/v1/client-entities/{set}/snapshot`, through the same OData read as the pages), a reload opens the stored file instead of parsing rows, queries and facets are SQL. A hub's 23,177 shipments with tags: first copy 12-16 s instead of 59-80 s, reload about 1 s instead of 20-29 s with the page blocked, filtered pages 6-40 ms. A client built without native SQLite keeps its copies in memory ([SQLite in the browser](../ui/client-entities.md#sqlite-in-the-browser-optional)).
- OData: a list reads the keys of its visible rows first (`Coworkee:OData:KeyedRowLimit`, default 50,000; one read of the keys, the count of the visible rows remembered for two minutes per user and group selection), then its page, `$count`, groups, facets and the pages of a local copy among them with every filter still on. A costly row filter runs once, for the keys, and a condition no index serves no longer scans the whole table: 282 of 19,007 hub shipments filtered by their pickup date took 117 s for a page with its count, now about 2.5 s; a page of 1,000 rows for a local copy took 36 to 46 s, now about 2 s (19,000 shipments in about a minute instead of more than ten).
- Header: spacers (*Add spacer* on the header page, ids `spacer`, `spacer-2`, ... in `Header.Items`) push the items after them to the other end of their bar; without one a bar keeps its items where they were. Menus of the app bars open away from the bar's edge and from the end of the bar the item sits at (`HeaderMenuOrigins`, cascaded to every item; the library's menus and the group picker read it).
- Themes: saving a built-in theme changes it for the organisation in place instead of creating a copy; it keeps its name and id, the tile shows *changed* and offers *Reset to the built-in look* (`POST /api/v1/themes/{id}/reset`).
- Themes: every tile has the switch *Users can choose it*, built-in themes too (`PUT /api/v1/themes/{id}/published`).
- Themes: `NavMiniMode` (every link, or the first level with a group's links in a menu on click or hover), `NavMiniDividers` (lines between the groups of the narrow navigation) and `NavBesideAppBar` (the navigation the full height beside the app bar, its head with the app's name and menu button).
- Header: the app bar at the top, right, bottom or left (`Header.Position`) and an optional second app bar at another edge (`Header.SecondPosition`), each with its own overflow menu; the header page shows the layout as a small picture and gives every item one menu instead of five buttons.
- App themes (`ThemingOptions.Themes`) bring `Options`, `Layout`, `Typography` and `CustomCss` over those of their base theme.

### Changed

- Client entities: a copy asks the server for the facets of all its rows (a read of the whole set) at most every 10 minutes instead of after every change; the counts stay the local rows'. A set the server changes every minute (a hub's shipments) no longer has its whole set read every minute.
- Client entities: the server's facets are kept with the copy; after a reload they count on the restored rows at once and are asked for again in the background.

### Fixed

- Client entities: a filter on a `TimeOnly` property with a time literal (`Start lt 12:00:00`) failed in the local copy and went to the server; it runs locally now.
- OData: the facets' read of the members by key dropped every query filter, also those of the navigations it read through (a label through a soft deleted or other target); it keeps them now.
- OData: facets of a bounded set read the keys of the matching rows first and their members by key. Read at once under a costly row filter (an organisation scope with an `EXISTS` over millions of rows), SQL Server tested the filter row by row: the facets of 11,772 hub shipments took more than five minutes and never reached the table or the local copy; now about 3 s (keys 0.7 s, members 2.7 s).
- Header: the floating assistant and the chat dock lay under a bar at the right or the bottom edge; they keep clear of side and bottom bars now, the full-size assistant fits between the bars. Side bars and the room the page leaves them mirror in a right-to-left layout.
- Client entities: rows with a collection among their properties (e.g. `Tags`) downloaded whole server rows; they select their own members now (a third of the data for a hub's shipments), the collection still comes through `$expand`.
- Client entities: a facet load that finished at once kept the copy from ever asking for the facets again.
- Themes: a changed theme or default reached only the open pages of theme managers; every page of the organisation applies it at once now.
- Themes: a theme the user chose kept its old look after someone edited it (the logo in the navigation, colours) and stayed after it was deleted, also for the one who edited it; it changes at once now, a deleted one gives way to the organisation's theme. The theme menu, the theme cards of the user settings, the command palette and the assistant drop deleted themes and show new ones at once (`ThemeService.ThemesAsync`).
- Localization: edited translations, a new default language and a language the user saved elsewhere reached open apps only after a reload; they show live now, also when the language menu is hidden. Only a language picked on the device is kept in the browser (`coworkee.culture.chosen`); the switched-off note names the language instead of its code.

## 0.0.1.64 (2026-11-05)

### Changed

- MudExRichTextEditor 9.5.7 (built on MudBlazor.Extensions 9.12.2).

### Fixed

- Theme editor: the options of `CoworkeeTheme` and of the app's theme class sat in the panel of the palette without grid items, so their first row was cut off at the top. They are a group of their own (*Appearance*), three to a row; logo and custom CSS keep the whole width.

## 0.0.1.63 (2026-11-05)

### Added

- Own theme class per app: `AddCoworkeeTheme<TTheme, TEditMeta>()` in the client makes every theme a `TTheme : CoworkeeTheme`; its properties are fields of the theme editor and are saved and loaded with the theme. `CoworkeeThemeEditMeta<TTheme>` is the base of the app's editor configuration (override `ConfigureAsync`, call the base). New apps get an empty `MyAppTheme` in `Theming/` and `MyAppThemeEditMeta` in the new `ObjectEdit/` folder of the client, registered in `Program.cs`. Existing apps: see *Upgrading older apps* on the [Theming](../modules/theming.md) page.
- The published chip of an own theme on the Themes page publishes or unpublishes it at once.
- Dialog options in one place: `DialogOptionsContributor` gives every dialog of the library its `DialogOptionsEx` per `DialogPart` (side sheet, right sheet, small, full screen, progress, list item); an app overrides parts with `AddSingleton<DialogOptionsContributor, MyAppDialogOptions>()`, the defaults stay as they were. With `AdminEditable` administrators edit each part under *Administration > User interface > Dialog options* (setting `Dialogs.Options`, live). See [Dialogs](../ui/client.md#dialogs).

### Changed

- MudBlazor 9.12.0, MudBlazor.Extensions 9.12.2 and Nextended 10.1.51; `coworkee update` moves the pinned packages.

### Fixed

- `CoworkeeDataTable` with pages: when the grid's first load was cancelled by a newer one (restoring the layout or the URL), the pager went on counting but no page loaded any more; MudDataGrid finishes setting itself up only after its first load returns. A cancelled load now returns the rows shown and the newer one brings its rows, also in the pages of a group.
- "The app could not start" after an update: the browser kept resources of the older build (cache storage, service worker) that fail their integrity check, so reloading failed again while a private window worked. A failed start now drops them and reloads once per session, and the reload button does the same.
- Live updates after a start without the realtime hub: when the hub did not answer while the app started, components that subscribe once (the layout's settings, so the app bar, theme and table mode) never followed a change until the page was reloaded. The client now retries the start in the background and then subscribes every followed topic; `RealtimeClient.SubscribeAsync` always keeps the handler.

## 0.0.1.62 (2026-11-04)

### Added

- Live watches can be switched off and on at runtime: `GET /api/v1/realtime/watches`, `PUT /api/v1/realtime/watches/{entityType}` (permission `Realtime.Watches.Manage`) and the component `RealtimeWatchSwitch EntityType="…"` for a page above a watched list.
- Client entities: the sync can be paused and resumed from the table's status menu (`ClientEntitySet.PauseSync`/`ResumeSync`); a paused download goes on where it stopped.
- Facet cap per set: `AddODataEntity<T>(…, o => o.MaxFacetRows(20_000))`. Above it the facets are counted on the first rows by key instead of left out, marked `FacetGroupDto.SampleRows`/`TotalRows`, and the table says so. A local copy asks for the facets of all its rows (`X-Coworkee-Facets-All-Rows`, honoured within `FacetRowLimit`). The global `Coworkee:OData:MaxFacetRows` keeps leaving them out.
- The mail template editor opens every template visually: Scriban statements, styled buttons, tables and other markup the editor would rewrite become protected chips that are saved byte for byte, so the text around them stays editable. Only a whole document such as the layout still opens in Source.
- Managed header: administrators order every app bar item (library and apps), put each in the bar or the overflow menu, or hide it, under *Administration > User interface > Header* (setting `Header.Items`, live). `AppBarItem` gets `Id` and `Title`.
- Assistant display (`Ai.Display`): floating button bottom right or left, app bar button, navigation entry or off; `Ctrl+I` works in every mode, `Ai.Enabled` still switches the assistant on. The navigation entry opens the window over the current page.
- New navigation group *Administration > User interface* with Themes and Header.
- The assistant knows the UI: the window sends the pages the user can see (with the new `CoworkeeNavItem.Description`), the current page, themes, color mode, languages and right-to-left with every message, so "how do I…" gets real click paths.
- Client actions: the assistant opens pages and switches theme, color mode, right-to-left and language in the browser (`client_action` stream event); apps add their own with `IAssistantClientAction`.
- Country flags ship with the client (flag-icons, MIT, no CDN): `<CountryFlag Code="DE" />`, `<LanguageFlag Culture="de-AT" />`, `CountryCodes.Resolve`, `LanguageFlags.Region` and `CoworkeeClientOptions.LanguageRegions`.
- `[CountryCode]` and `[FacetDisplay("…")]` mark how a property's values show. Facets carry it as `display`, and facet options, chosen-facet chips and table cells render through `CoworkeeClientOptions.ValueRenderers`. "country" (flag and text) is built in; apps add their own, e.g. "port".
- The language menu shows flags with native and English names; the Languages page and the mail template language chips show flags. The theme menu shows color swatches and a light/system/dark switch; theme cards preview the app bar, surface and text of each theme.
- The header page arranges the app bar by drag and drop, within and between the areas bar, overflow menu and hidden; the buttons remain for the keyboard.
- Header items can be limited to users with any or all of a set of permissions (`Header.Items`: `permissions`, `match`), chosen in a sheet from the right; the app bar follows permission changes at once. The account menu stays visible to everyone.
- The switch of the active role is the header item `role` instead of a part of the account menu: administrators move it, put it in the overflow menu (`ActiveRoleMenu Nested`), hide it or limit it to permissions.
- New `PermissionPicker` component: grouped, searchable selection of permissions with removable chips and an optional any/all choice; `PermissionStore.HasAsync(permissions, match)` checks such a selection.
- Custom permissions: administrators of the system organisation create permissions of their own (`Custom.*`, display name, description, group) on *Administration > Identity > Custom permissions*, grant them through roles, groups or users like any other, and delete them after a preview of the roles, groups and users that lose them; deleting removes every grant. Endpoints under `/api/v1/identity/permissions/custom`, new `ICoworkeeApi` members, `IStoredPermissionDefinitions` for definitions added at runtime.
- Apps need a migration: new table `cw.CustomPermissions`. Without it the code permissions keep working and a warning is logged.

### Changed

- Tables query the rows before the facets: a local copy that answers at once no longer keeps the last page on the screen while it counts.
- `CoworkeeDataTable`: with pages and grouped, the rows scroll inside the table (`FillHeight`, default on, at most `Height`, default `EndlessHeight`): column headers and the group zone stay on top, the horizontal scrollbar and the pager or total stay in view, another page starts at its first row; below 960 px the page scrolls as before.
- `CoworkeeDataTable` renders its rows less often: facets arriving after the rows, switching a facet's view and the total of a new endless query no longer render the rows again.
- `IAssistantApi.SendAsync`/`ConfirmAsync` take an `AiUiContext? ui` parameter: apps that implement the interface themselves add it.
- ASP.NET Core, EF Core and Microsoft.Extensions packages 10.0.14 (security fix of `Microsoft.AspNetCore.Components.WebAssembly.Server` 10.0.13). Apps: raise their 10.0.13 references too.

### Fixed

- A group opened again after the filter changed showed rows of the filter before.
- `CoworkeeDataTable`: the pager of an opened group sits left; the table's own pager stays right.
- Choosing a theme no longer shows the organisation's theme for a moment: `ThemeService.LoadAsync` reads the current theme and the user's choice first and applies once.

## 0.0.1.61 (2026-11-03)

### Added

- `AddRealtimeWatch<T>(permission, watch => watch.CreatedAt(…).ChangedAt(…))` sends changes of rows another system writes (an external database) like changes made in the app: while someone follows `type:X` or `entity:X:{id}`, the set is read by its dates every interval (30 s) and every newer row goes out as `EntityChanged`. Tables, detail pages and client entity copies follow it; following needs the given permission. See [Realtime](../modules/realtime.md#entity-changes).
- Cached queries: `ICachedQuery.CacheRefreshAfter` answers an older entry at once and loads it again in the background (stale while revalidate), `CacheByGroups` keeps an entry per selection of active groups. The new settings group *Cache* (`CacheSettings`) holds the defaults of queries without own times: duration, refresh after and a switch for background refreshing (`ICacheDefaults`).

## 0.0.1.60 (2026-11-03)

### Added

- Saved settings reach every open page at once: a `SettingValue` change is sent on `SettingTopics.Global` (`global:settings`), `SettingTopics.Tenant` (`tenant:settings`) or the user's topic, and `CoworkeeLayout` (`SettingsLiveUpdates`) reloads the client settings; `ThemeService.Changed` follows. The assistant button and the palette's *Ask the assistant* appear and go as an administrator switches the assistant on or off, or `Ai.Chat` is granted or taken, without a reload.
- Grouped data tables: a group shows *loading* until its first rows arrive; its rows stay kept when it closes, and opened again (or after a reload of the table) it shows them at once and loads them again in the background.

### Fixed

- The assistant failed with *The AI provider could not answer* when a request had a property named like a JSON schema keyword (`Properties`, `Items`): its input schema got `"type": {}`, which Anthropic refuses (*input_schema: JSON schema is invalid*). Only subschemas are opened now.

## 0.0.1.59 (2026-11-03)

### Added

- `IODataClient.GroupAsync<T>(entitySet, filter, by)`: the groups of a filtered set with their counts (`$apply=groupby` on the server, counted in the browser for client entities). The default implementation throws `NotSupportedException`.
- `LocalQueryNotSupportedException`: a client entity copy cannot answer a query; `ClientEntitySet<T>.MeasureAsync` throws it for the browser side.

### Breaking changes

- Grouped data tables list every group of the whole result with its count, closed, and load a group's rows when it opens (pager per group, or scrolling inside it in endless mode), instead of grouping the loaded page. Own decorators of `IODataClient` forward `GroupAsync` to the client they wrap, otherwise their tables group the loaded page as before. See [groups of the whole result](../ui/data-table.md#columns-grouping-and-views).
- `DataTablePages<T>.GetAsync` takes the load with a cancellation token and a token of its own: a chunk goes on loading when its caller stops waiting, and only a new query ends it.

### Fixed

- Endless tables requested the same chunk again and again while scrolling (each scroll step cancelled the chunk before and nothing was kept): over 1,000 requests for a few seconds of scrolling, and rows flickering *Loading*. A chunk now loads once.
- An endless table restored grouped (saved view, link) started scrolling, loaded rows and never switched to the grouped layout.
- *Compare and choose the faster* of a client entity failed with *Comparing failed: … has no property …* for a query the copy cannot answer; it now says so and searches on the server.
- Apps referencing Coworkee as projects (`CoworkeeReferenceMode=Project`) import the build targets of the packages (`Coworkee.Client.Blazor`: all ICU data, without which the Blazor client stopped at start in a non-English browser; `Coworkee.CodeGen`). Template: `Directory.Build.targets`; existing apps add the two `Import` lines.

## 0.0.1.58 (2026-11-02)

### Added

- Nextended's `[RegisterAs]` registers services without `services.Add...`: `AddCoworkeeModules` (and `AddCoworkee`) registers the marked classes of every module assembly after `ConfigureServices`; `services.AddRegisterAsServices(assemblies)` for other assemblies. Registrations that are there already are not added twice. `Coworkee.Core` references `Nextended.Core` now. See [registering with an attribute](../fundamentals/modules.md#registering-with-an-attribute).
- `[Get]`, `[Post]`, `[Put]`, `[Patch]`, `[Delete]` and `[Endpoint]` (`Coworkee.Application.Messaging`) on a request map it as an endpoint that dispatches it; `UseCoworkee` maps the requests of all module assemblies (`MapCoworkeeRequests`). See [routes on the request](../web/http-api.md#routes-on-the-request).
- MVC controllers of module assemblies are found and mapped: `AddCoworkee` adds the controllers with the module assemblies as application parts, `UseCoworkee` maps them.

## 0.0.1.57 (2026-11-01)

### Added

- `Coworkee:Bff:Pwa:Icons` (`BffPwaOptions.Icons`): several icons of the web manifest with sizes, type and purpose (e.g. 192 and 512 pixel PNGs), listed instead of the single `Icon` while the theme has no logo. See [phones and PWA](../ui/phones-and-pwa.md).
- `IClientEntityChangeLog.Touch<T>(ids)` (L86): reports rows to the client entity copies when no `ChangedAt`/`ModifiedAt` column moved (a tag put on through a link table); stored in the new table `cw.ClientEntityTouches` with the command and read by the changes feed for the rows the reader sees. The library's tags, ratings, comments and reactions report entities registered typed (`Tags<T>(...)`) themselves. A copy last refreshed more than `Coworkee:ClientEntities:ChangeLogLifetime` (30 days) ago downloads again. Add a migration. See [changes no date sees](../ui/client-entities.md#changes-no-date-sees).

### Breaking changes

- Apps need a migration: new table `cw.ClientEntityTouches`, new column `cw.OutgoingMails.SendingSince` (nullable) with an index on `Status`.
- Registered `IAttachmentScanner`s also check uploads to the files (`EntityType` `StoredFile`); a scanner that reads `EntityType` as a record type handles it.
- A client entity copy last refreshed more than `Coworkee:ClientEntities:ChangeLogLifetime` (30 days) ago downloads again.

### Fixed

- MudBlazor tab badges cut off counts longer than one digit. `CoworkeeLayout` now places them inline after the tab text in every app; apps with an own rule for it (CoShipee's `.mud-tab .mud-tab-badge`) may drop it.
- Template: the permissions `Catalog.Brands.Export`, `Catalog.Products.Export` and `Documents.Export` were defined but checked nowhere (exports need the set's view permission); they are gone from new apps and from the roles a spec may name. Existing apps may delete them. The home page and `AppDescription` read *Modular business app for Blazor, built on Coworkee.*
- Texts: `Reports.Share` reads *Share reports with roles, groups or the organisation*, the social permission group *Comments, tags, ratings and reactions*; the settings *Daily e-mail with unread notifications*, the head script fields and the group *Accounts* are translated in every shipped language.
- Blazor exports of `CoworkeeDataTable` sent neither the column titles nor `ExportFileName` to the server, and `ExportQueries.Of` joined the columns without escaping. The export now asks with the titles as headers and `fileName=`; `ExportQueries.Of(format, query, columns, fileName)` takes `(Field, Header)` pairs and escapes them with `ExportColumns.Format`.
- The settings form showed settings with their own permission (the head script) to everyone with `Settings.Manage`; saving them answered 403. The definitions leave such a setting out for whoever lacks its permission.
- A mail stayed in `Sending` forever when the sender stopped between the provider and saving the result. The recurring job `coworkee-mail-stuck-sending` now fails such mails after `Coworkee:Mail:SendingTimeout` (30 minutes) with *Delivery unknown*, never sending them twice; `Coworkee:Mail:RetryStuckSending` queues them again instead (a mail may then arrive twice). New column `cw.OutgoingMails.SendingSince` and an index on `Status`: add a migration. See [Delivery and the mail log](../modules/mailing.md#delivery-and-the-mail-log).
- Uploads to the files (Files page, `POST /api/v1/files`) were never virus-scanned. The registered `IAttachmentScanner`s check them now before they are stored (`FileUploads.EntityType`, the folder as `EntityId`); a refused file answers 400. An app whose scanner reads `EntityType` or `Slot` as a record type handles `FileUploads.EntityType`.
- A `CacheScope.Global` entry was tagged with the organisation of the caller who loaded it: a command in another organisation never invalidated it. Global entries carry a global tag now, which every `IInvalidatesCache` command clears; entries of the other scopes stay per organisation.

## 0.0.1.56 (2026-10-31)

### Security

- Erasing a user (`DELETE /api/v1/identity/users/{id}`, *delete my account*) left personal data behind, and the personal data export missed tables. Now every module contributes, and the library ends each erase with `PersonalDataReferences`; see [the rules of an erase](../security/privacy.md#the-rules-of-an-erase):
    - His id leaves `CreatedBy`, `ModifiedBy` and `DeletedBy` of every table (the app's too), the audit trail (`ActorId`, `ImpersonatorId`, his id and address inside recorded values), the snapshots and the outbox; `PersonalDataSubject.ErasedUserId` (the empty GUID) takes its place. The values recorded for the records erased with him (his user, rows keyed by his id, records he created that no longer exist, e.g. deleted comments) become `***`; the trail itself stays.
    - Reporting: favourites, subscriptions (the subscription job kept mailing an erased user), dashboard choices and private reports are exported and erased; shared reports stay, owned by the placeholder.
    - Import and export: jobs (with their files) and scheduled exports are exported and erased, a running export whose job is gone stops, his address leaves the recipients of the other schedules of his organisation.
    - Webhook endpoints he owned are switched off (deliveries ran as him). Workflow decisions and history keep the placeholder without his comments; pending approvals waiting only for him are overdue at once (timeout state or escalation), already escalated ones are cancelled.
    - His access tokens (file and download links) are deleted and his cached session stamp is dropped on every instance (his tokens worked up to a minute longer). Keys of others he revoked and clean-ups he started keep the placeholder, group chats he created stay with the placeholder as creator, his registration documents are deleted with their content.
    - Notifications, follows and preferences of his id are erased in every organisation, mails only in his organisation (another user of another organisation with the same address lost his mail log).
    - The export adds `profile.claims`, `permissionGrants`, `resourcePermissions` and `selectedGroups`, the chat's `memberships`, `reactions` and `attachments`, `activity` entries made while signed in as someone else, and the sections `reports`, `dataTransfers`, `webhooks` and `workflows`.

### Added

- `PersonalDataSubject.ErasedUserId`: the placeholder own contributors write where an erased user's id stood. `tests/Coworkee.ArchitectureTests/PersonalDataCoverageTests` fails for a column of the library that names a user without a contributor.

### Breaking changes

- The `chat` section of the personal data export is an object (`messages`, `memberships`, `reactions`, `attachments`) instead of the list of messages.
- An erase writes `PersonalDataSubject.ErasedUserId` into `CreatedBy`, `ModifiedBy` and `DeletedBy` of the app's tables too; code that looks the creator up finds no user for it.

### Fixed

- The file system blob storage failed to delete a key whose directory did not exist, the S3 storage one whose bucket did not exist yet; both ignore it now like any other missing blob (erasing a user whose chat file was already gone answered 500).

## 0.0.1.55 (2026-10-31)

### Added

- Group-scoped administration: with `Coworkee:Identity:GroupScopedAdministration` (off by default) administrators without `Identity.Groups.ScopeAll` who are no administrators of the organisation reach only their groups, the sub-groups and their members. Everything else answers 404 and is absent from lists, counts, lookups, the OData sets `Users`, `Groups`, `ApiKeys`, exports, search and AI tools; users who also belong to a group outside the reach are changed only by someone reaching all their groups (`403 identity.user_other_groups`), members added must belong to no group outside the reach, and top-level groups, the default group and roles need the whole organisation (`403 identity.all_groups_required`). The default group counts for nobody. `AdminReach` (`Coworkee.Identity.Users`) applies the rules in own requests; `AccountGuard.CheckUserAsync` includes them. See [group-scoped administration](../security/permissions.md#group-scoped-administration).
- `CreateUserRequest.GroupIds`: the groups a new user joins (needs `Identity.Groups.ManageMembers`); the *New user* dialog offers them to who manages members. A group-scoped administrator names at least one of his groups.

### Breaking changes

- `CreateUserRequest` has a new last parameter `GroupIds`: source that calls the constructor compiles unchanged, assemblies compiled against the old one are rebuilt.

## 0.0.1.54 (2026-10-31)

### Security

- User managers could take over stronger accounts: setting the password, the e-mail address (also as confirmed), resetting two-step verification or passkeys, removing external logins, changing the avatar or groups of another user checked only the organisation. Now every request that changes or controls another user's account (also update, unlock, roles, own grants, language, invitation and password reset mails, revoking his API keys, group memberships) is refused while the user holds a permission the caller lacks (`403 identity.user_more_permissions`), and only administrators act on administrators (`403 identity.admin_role_restricted`). See [other accounts](../security/permissions.md#other-accounts-and-handing-out-permissions).
- Permissions could be handed out further than the caller held them: grants to roles, users and groups, user and group roles, new users with roles, group memberships, deleting roles and groups and resource grants now need every permission involved (`403 identity.grant_restricted`); the administrator role needs an administrator. Default roles and the default group stay assignable.
- Service clients: changing, rotating the secret of or deleting a client now needs every permission it has (`403 auth.client_rights_restricted`), so a client manager cannot take over a stronger client.
- Extended attributes now check that the caller sees the row through the entity's OData set (its permission and row filters): `GET`/`PUT /api/v1/attributes/{type}/{id}` on a hidden row answers 404.
- Erasing a user removes his notification preferences, followed records and pending notification mails; the personal data export lists them and the digest state.

### Added

- Client entities: `ClientEntitySettings.Expand` and `[ClientEntity(Expand = "...")]` name the navigation properties a copy carries, whatever query reaches the set first; `ClientEntitySettings.ParallelPages` (default 1) sets how many download pages load side by side. Name the expand of a set whose list uses `$expand` while other pages (a dashboard, a map) query it too; without it the first query of a visit decided, and the copy was downloaded again whenever another page came first.
- `IClientEntityStorage.DeleteAsync(prefix)` (default: removes nothing) and `IClientDataCache.ScopeChangedAsync()` (default: `ClearAsync()`). An own `IClientEntityStorage` should implement `DeleteAsync`, so the pages of finished downloads and the copies of old group selections leave the browser storage.
- The change feed of client entities takes a `skip` and then answers many changed ids in pages (`more`); its answers carry the caller's partition, the manifest an `owner` (user, tenant, client).

### Breaking changes

- Client entities download by the last id (`$filter=Id gt …&$orderby=Id&$top=…`) one page after the other instead of two pages at a time with a growing `$skip`, which made the database read all earlier rows again for every page (minutes per page on large tables with a row filter), so downloads timed out and started over. They keep their state between visits: every round of the download is kept in IndexedDB, so a reload or a failed request goes on where it stopped; a complete copy only takes changes afterwards, also after more than `BatchSize × 10` changes, which no longer start the download from zero. A restored copy answers once its first update arrived (rows deleted or no longer visible meanwhile are gone by then); the table asks the server until then.
- A new group selection keeps the copies of the last three selections in the browser storage instead of dropping every copy; switching back takes only the changes. A copy of another user or tenant leaves the device before anything is read, also without a sign-out; a copy the server answers for another partition (selection changed in another tab) is dropped at once. After the update, the stored copies of older versions are downloaded once more.
- Queries with `$select` or `$search` start no copy of a client entity set.

### Changed

- Signing out or locking an administrator as a non-administrator answers `identity.admin_role_restricted` instead of `identity.admin_session_restricted`.
- `ExtendedAttributeRegistration` built by `AddExtendedAttributes<T>` checks visibility through `ODataEntityAccess.VisibleIdsAsync<T>`; the package references `Coworkee.OData`. Setting extended attributes leaves the save to the pipeline.

### Fixed

- A copy saved while its columns were still being indexed could be written inconsistently and failed to restore, so the next visit downloaded it again. No action needed.

## 0.0.1.53 (2026-10-31)

### Added

- The head script: `AddCoworkeeHeadScript(perOrganisation)` turns on the settings `HeadScript.Enabled` and `HeadScript.Value` (HTML with script tags, e.g. a tag manager), the permission `Settings.HeadScript`, the anonymous `GET /api/v1/settings/head-script` and `PUT /api/v1/settings/head-script`; the Blazor layout adds the script to the page. Off unless the app calls it. See [Settings](../modules/settings.md#head-script).
- `SettingDefinition.Permission` (`Add(..., permission: ...)`): a setting changes only with that permission on top of the scope's own rule, also through `PUT /api/v1/settings/{scope}`.

### Fixed

- Two modules adding permissions to the same group listed that group twice in the permission tree. No action needed.

## 0.0.1.52 (2026-10-31)

### Added

- `ODataEntityOptions<T>.MaxNodeCount(int)`: a node limit of one set instead of `Coworkee:OData:MaxNodeCount` (`AddODataEntity<T>(set, permission, o => o.MaxNodeCount(1000))`), for clients that send long filters to that set only. It applies to the set's routes, `IODataQueries`, exports, facet filters, row filters of code and `query_data`; `ODataLimits.Settings(services, entityType)` reads it. No action needed. See [OData](../web/odata.md).

### Fixed

- A mailed export (`POST /api/v1/data/{set}/export/mail`, `DataExports.MailAsync`) and a streamed export whose client left wrote the file but never sent the mail: the export job did not save the mail the mailing module queued. No action needed.

## 0.0.1.50 (2026-10-31)

### Added

- `IODataQueries.QueryAsync<T>(route, entitySet, query, facets, facetContext, ct)` takes a table's facet choice (`FacetContextDto`), so an endpoint that answers a `CoworkeeDataTable` through a `DataSource` counts each facet group without its own choice, as the OData route does. The overload without it stays. See [OData](../web/odata.md).
- Column headers in exports: `columns=Field:Header` heads a CSV or Excel column (`columns=Number:Shipment no.,Total`); a backslash escapes a comma or colon in a header, and `ExportColumns.Format`/`Parse` (`Coworkee.Contracts.Data`) write and read the value. `columns=Field` works as before, JSON keys stay the field names, and mailed and scheduled exports keep the headers with their query. See [import and export](../web/import-export.md#export).
- `route=<prefix>` in an export query (`GET .../export`, the stream, `MailExportRequest.Query`, `SaveExportScheduleRequest.Query`) reads `$filter`, `$orderby` and `columns` in the names of that OData route (`AddCoworkeeODataRoute`), e.g. `route=rt/odata&$filter=creationTime gt 2026-01-01T00:00:00Z`; without it the names of `/odata`. The set's permission, hidden and protected properties, row filters and groups apply as on the route; an unknown route answers `400`.
- `fileName=<name>` in an export query names the file of the export and of the stream (job, ready line, download), as `MailExportRequest.FileName` does for mailed exports; the extension of the format is added.

### Changed

- A streamed export with more rows than `BackgroundExportRows` no longer ends with `queued`: the stream follows its export job with `reading` and `writing` lines (the job saves its progress every second) and ends with `ready` and the one-time link, or with `failed` or `cancelled`. When the client leaves meanwhile, the file is mailed to the export's owner once it is written. **Action:** a migration for the new column `cw.DataTransferJobs.MailOwner` (`coworkee migrations add <Name>`). A client that took `queued` as the last line reads on to `ready`.

## 0.0.1.49 (2026-10-30)

### Added

- Reports can be shared with groups (`ReportVisibility.Groups`, `SaveReportRequest.GroupIds`, `ReportDto.GroupIds`): the members of those groups and of the groups above them see the report, through the user's active groups, so a group selection narrows the report list. The owner shares only with own groups and those below them (`GET /api/v1/reports/groups`, `Reports.Share`); the report settings offer the groups. **Action:** a migration for the new column `cw.Reports.GroupIds` (`coworkee migrations add <Name>`). A client that saves a report it read passes `GroupIds` along, or a report shared with groups is refused (no groups chosen). See [reporting](../modules/reporting.md#sharing-and-permissions).
- `ReportWidget.AppData`: JSON an app or a second client keeps with a widget; reports neither read nor change it (at most 20,000 characters per widget).

## 0.0.1.48 (2026-10-30)

### Changed

- A facet request counts the filtered set once before its facets (with `FacetRowLimit` or `MaxFacetRows` on), and the in-memory read has no `TOP` any more: a `TOP` of more rows than match let SQL Server plan for rows found early and scan the whole table (28 s instead of 4 s for 12,000 of 1.2 million rows). `MaxFacetRows` checks with that count. No action needed.

## 0.0.1.47 (2026-10-30)

### Added

- `Coworkee:OData:MaxFacetRows` (`CoworkeeODataOptions.MaxFacetRows`, default 0 = off; below 0 stops the start): a filtered set of more rows than this gets no facets, checked with one count that stops after that many rows. For lists that may reach the whole database, where every facet group would read all of it. See [OData](../web/odata.md#how-facets-are-counted).

## 0.0.1.46 (2026-10-30)

### Added

- `Coworkee:OData:FacetRowLimit` (`CoworkeeODataOptions.FacetRowLimit`, default 50,000; 0 turns it off, below 0 stops the start): facets of a filtered set of at most that many rows come from one read of the members they need and are counted in memory (list groups of the row's own properties, ranges, computed options) instead of one query per group. Groups through a navigation, `Guid`, date and decimal list values, `double`/`float` ranges, chosen groups and larger sets stay database queries, all list groups in one. See [OData](../web/odata.md#how-facets-are-counted).

### Changed

- `CoworkeeDataTable` loads its facets in a request of their own (`$top=0`, no `$count`, no `$expand`): the rows no longer wait for them, and another page or sort no longer recomputes them; a filter, search, facet or group change and `ReloadAsync()` ask again. Its row requests carry no `Facets` and no facet context any more: an `IODataClient` or `ITableDataSource` that answered facets with the rows gets them in the separate `Top = 0` query. See [data tables](../ui/data-table.md#facets).
- The server no longer counts Nextended's date presets (`FacetRangeDefinition.Presets`), which never reached the response, and counts no facet group the user does not get (denied or refused).

### Fixed

- A date range facet on a `DateTime` property (`FacetType.DateRange`) failed every facet request of its set with `500`.

## 0.0.1.45 (2026-10-30)

### Added

- `Coworkee:OData:MaxNodeCount`, `MaxAnyAllExpressionDepth`, `MaxExpansionDepth` and `MaxOrderByNodeCount` (`CoworkeeODataOptions`, defaults OData's 100, 1, 2 and 5): OData's query limits on the entity sets of every route, in `IODataQueries`, exports, `query_data`, facet filters, row filters of code (`ODataEntityAccess.FilteredAsync`) and reports (computed sources included; `ODataLimits.Check(options, services)` for own code), e.g. for a search that ORs `contains` over many fields; a value below 1 (below 0 for `MaxExpansionDepth`) stops the start. See [OData](../web/odata.md).
- `HybridCache.GetOrCreateInScopeAsync(services, actor, key, (services, ct) => ...)` (`SharedCacheLoad`): a cache load that reads scoped services, in a service scope of its own as `actor`; `CurrentUserScope.Capture(user)` keeps a user beyond his request. See [messaging](../fundamentals/messaging.md#caching).

### Changed

- The handler of an `ICachedQuery` runs in a service scope of its own, as the calling user, with the cache's cancellation token (canceled only when every waiting request is): scoped state the request set before the query is not visible to it. Inside an open transaction of the app's DbContext (`IUnitOfWork.InTransaction`, new, default `false`) cached queries and the shared loads of permissions, settings, features and texts skip the cache: they read through the caller's context and transaction, so a grant written earlier in the transaction counts, and nothing of it is cached. See [messaging](../fundamentals/messaging.md#caching).
- Cached queries keep an entry per culture (`CurrentCulture` and `CurrentUICulture`), so the first caller's language no longer answers everyone, and with `CacheScope.Tenant` signed-in users without a tenant no longer share the entries of anonymous callers. Invalidation by tag reaches both as before. See [messaging](../fundamentals/messaging.md#caching).

### Fixed

- Requests waiting on the same cold cache load (a user's or a client's permissions, settings, features, texts, the auth server's branding and clients, cached queries) failed with `500` ("Connection is not open", a disposed context, a canceled task) when the request that started the load ended first, e.g. a browser aborting it on navigation. Every shared load runs in a scope of its own now.
- A failed cached query reached the other requests waiting on it as a success with an empty value; they get the failure now.
- `SelectGroups` (`PUT /api/v1/identity/groups/mine/selection`) twice at once for one user: the same selection failed with `500` on the primary key, different ones could be stored mixed or fail with `500`. They run one after the other now: both succeed, the later one stays.
- A cached query whose load outlived the request that started it computed with the user's full rights instead of the API key's scopes, and `CacheScope.User` entries were shared by a user's session, his API keys and a "Log in as" session. `HttpCurrentUser` takes the key and its scopes from the captured caller once the request is gone (a key's limits are never dropped), a user's entry is kept per key or session and per impersonator, and the group selection applies to a captured session (`ICurrentUser.IsInteractive`, new, default `false`).

## 0.0.1.44 (2026-10-29)

### Added

- Session revocation passes `IRealtimePublisher`: every instance publishes `SessionRevoked` on `user:{id}` with the `SessionRevokedPayload` (reason, `KeptSessionId`) once per session change, so a decorator can end the connections of an app's own hub. `RealtimePublisher` sends that type to no group; `RealtimeHub` clients get exactly what they got before. Decorators that forward every message see the new type. See [clients](../security/clients.md#signing-out-everywhere-and-locking).
- `CoworkeeApp.SpaClientUrl(resource)`: the public address of a single-page app added with `AddSpaClient` (its endpoint while running, the parameter `{name}-{resource}-url` in a deployment), e.g. for the app's own `APP_URL`; a resource without `AddSpaClient` throws. See [Aspire](../hosting/aspire.md#single-page-apps).

### Changed

- OData: the service document (`/odata`) and `$metadata` need a sign-in on every route (`/odata` and every `AddCoworkeeODataRoute`), anonymous requests get `401`; they listed every set and property name of the model to anyone. Both are served on every host now (before only where MVC's part discovery found OData's assembly). `Coworkee:OData:AnonymousMetadata` (`CoworkeeODataOptions.AnonymousMetadata`, default `false`) opens them again. Clients that read `$metadata` anonymously send a token or set the option. See [OData](../web/odata.md).
- The log and trace redaction of `AddCoworkee` and `AddCoworkeeBff` (`SecretPaths`) masks secrets in query strings too: `access_token` (SignalR WebSockets), `id_token`, `id_token_hint`, `refresh_token`, `token`, `code`, `code_verifier`, `client_secret`, `client_assertion` and `password` show `***` in the request lines of `Microsoft.AspNetCore.Hosting.Diagnostics`, HttpClient and YARP logs and the url tags of activities. See [files](../modules/files.md#temporary-links).
- Machine translation of missing texts starts from a key's English text when the key is none (imported texts keyed `{Resource}::{key}` with an `en` resource), not from the key itself. See [localization](../modules/localization.md).

## 0.0.1.43 (2026-10-29)

### Added

- OData routes with other names: `services.AddCoworkeeODataRoute("rt/odata", route => route.CamelCase().Alias<IAudited>(e => e.DateCreatedUtc, "creationTime"))` maps a second route over the same sets with its own model (lower camel case names, aliases per type, base class or interface; `FacetsOnWildcardPrefer(false)` builds facets for `"cn.facets"` only). Every check of `/odata` runs on it the same way: set permissions, row filters, `Hide`/`HideUnless`, computed properties, the navigation scope, the query checks, `$search` and facets, whose fields, value paths and option filters come in the route's names. `ODataRoutes` lists the routes; `ODataRouteProfile.AliasOf(type, property)` gives the alias for own JSON. See [OData](../web/odata.md#routes-with-other-names).
- `IODataQueries.QueryAsync<T>(route, entitySet, query, facets)` runs an OData list query of a route in process for the current user, through the steps of the HTTP route: typed rows (members the route would not write at their default) or shaped rows by the route's names, the count and the facets; refusals come back as `Forbidden`, `Validation` or `Unauthorized` errors. See [OData](../web/odata.md#queries-in-code).
- `Coworkee:OData:ModelFromEntityFramework` (`CoworkeeODataOptions.ModelFromEntityFramework`, default `false`): keys and properties of the OData model come from the EF model of each set's DbContext, external ones included. Types the sets reach are keyed by their EF primary key (a `{Type}Id` property next to an inherited `Id` no longer turns the type into a complex type), and properties EF maps without a public setter are in the model of registered sets, each logged at startup. See [OData](../web/odata.md#the-model-from-ef).
- Single-page apps in the app host: `app.AddSpaClient(clientId, resource, endpointName, scopes, apiScopeAliases, allowImpersonation, redirectPath)` registers a public client with PKCE at the resource's address (a parameter `{name}-{resource}-url` in a deployment), API scope aliases with the api's audience and the resource's origin in the CORS policy. See [Aspire](../hosting/aspire.md#single-page-apps).
- CORS: `UseCoworkee` applies a policy of the exact origins in `Coworkee:Cors:Origins` (`CoworkeeCorsOptions`) on the auth server and the APIs, without credentials; endpoints opt into credentials with `.RequireCors(CoworkeeCors.WithCredentials)`, the realtime hub does. Without origins nothing cross-origin is allowed, as before; `*` stops the start. See [clients](../security/clients.md#cross-origin-calls-cors).
- `PUT /api/v1/identity/me/password` (`ChangeMyPassword`, `ChangePasswordRequest`): the own new password with the current one, password rules and history, refused while impersonating and with API keys, rate limited like the sign-in; the other sessions end, the calling one keeps its tokens. See [clients](../security/clients.md#changing-the-own-password).
- The auth server announces the end of an impersonation (stop, expiry, revocation, sign-out) through the outbox (`ImpersonationEnded`); the api sends `RealtimeEventTypes.ImpersonationEnded` (`ImpersonationEndedPayload`) on the impersonating sign-in's topic `session:{sid}` (`RealtimeTopics.Session`), which only connections with that `sid` in their token may follow; `SessionGuard` signs the impersonated Blazor session in again. The ID token carries `sid` now, `BffUserDto.SessionId` passes it to the client. See [identity](../features/identity.md#log-in-as-another-user).
- `IPermissionChecker.UsersGrantedAsync` and `GET /api/v1/identity/users/granted` (`GetUsersGrantedQuery`, `Identity.Users.View` and `Identity.Roles.View`): users of the organisation holding a permission through roles, own and group grants, optionally within a group subtree, paged. See [permissions](../security/permissions.md#who-holds-a-permission).
- `Coworkee.Testing`: the test authentication takes further claims after the third part of its header (`Test {user};{tenant};;sid=abc`).

### Changed

- OData `$search` on a set with a global search (`AddGlobalSearch<T>(s => s.Fields(...))`) looks in those fields only, not in every string property; an `ISearchable` entity keeps its own fields. List a field there when the table's search box should find it. See [global search](../web/global-search.md).
- The AI tool catalog takes requests with several `[RequiresPermission]` attributes (the tool needs all of them); before, such a request broke the assistant and MCP with an `AmbiguousMatchException`.
- `SessionRevokedPayload` has `KeptSessionId`: after a password change it names the session that goes on, `SessionGuard` of that session ignores the message; single-page apps ignore a `SessionRevoked` carrying the `sid` of their own token.
- The BFF removes `Access-Control-*` headers from its answers, so CORS headers the API sets for single-page apps never apply to the cookie session forwarded through the web host.
- PKCE takes only `S256` challenges: the auth server accepted `plain` (and a challenge without method) before; clients that send `plain` get `invalid_request`.
- A wrong current password of `POST /api/v1/identity/me/email` counts towards the lockout again (the failed attempt was not saved).
- `IPermissionChecker` has the new member `UsersGrantedAsync` with a default (an empty page). New endpoints: regenerate OpenAPI snapshots and SDKs.

## 0.0.1.42 (2026-10-28)

- Notifications: a type can name a mail template (`mailTemplate`, `NotificationType.MailTemplate`) that renders the new `NotificationMessage.MailModel` next to `user` and `notification`; the model is stored as JSON with the waiting mail, so delayed and debounced mails render the latest one. `resolveEmailDelay` (`NotificationType.ResolveEmailDelay`) takes the mail delay from the sender's tenant (a feature, a setting) when a notification is published; `emailDelay` stays the default. `NotificationMailModel.Sample(model)` is the sample for the preview and test mail of such a template. Mail models may be dictionaries or JSON objects. A model with a reserved top-level name (`user`, `notification`, layout variables, Scriban builtins) or larger than 32 KB is refused with an `ArgumentException`; a template that fails to render falls back to the generic mail, and a failing delay resolver to `emailDelay`, without failing the command. Types without a template, and mails without a model, mail as before. Apps need a migration. See [realtime](../modules/realtime.md#mail-templates-per-type-and-delay-per-tenant).
### Added

- Data tables export through the progress stream: the export menu of `CoworkeeDataTable` opens a dialog with the phase, rows and percent and *Cancel*, saves the file through the one-time link, hands a queued export to the background notice and shows `429`, invalid queries, timeouts and lost connections as errors. `ExportProgressDialog.ShowAsync` for own buttons, `FileDownloader.DownloadUrlAsync`. Every line of the export stream carries the `jobId` (the first one at once), reading and writing lines `rows` and `total` (`DataExportProgress.Rows`, `Total`). `POST /api/v1/data/jobs/{id}/cancel` (`IDataTransferApi.CancelJobAsync`) cancels a running or queued export of the user: status `Cancelled` (new member of `DataTransferStatus`), no file, no mail, its slot is free at once, also when another instance runs it (it reads the job row every two seconds); the stream ends with `cancelled`. The dialog's *Cancel* uses it. See [import and export](../web/import-export.md#progress-stream-and-one-time-download).
- `IPreparingInterceptor` (Coworkee.Infrastructure.Persistence): an interceptor that runs before every other one, so rows it adds while saving are audited and published; see [persistence](../fundamentals/persistence.md#audit-trail).
- Group owners, a default group and default roles: owners on the Members tab and `PUT /api/v1/identity/groups/{id}/owners`, the default group on the General tab and `GET`/`PUT /api/v1/identity/groups/default`, *Given to new users* on the role page; every new user (admin, registration, external sign-up and single sign-on, seed, new tenants, app imports through `UserManager`) gets the default roles and joins the default group. Seed: `IdentitySeedOptions.DefaultGroup`, `SeedRole.IsDefault`. See [identity](../features/identity.md#owners-default-group-and-default-role).
- OData: `Coworkee:OData:CaseInsensitive` (`CoworkeeODataOptions.CaseInsensitive`, default `true`) sets whether names in OData URIs match regardless of case (`$filter=name eq 'x'`); it was always on. See [OData](../web/odata.md).
- OData: `AddODataEntity<T>(..., o => o.Key(c => c.IsoCode))` registers a set keyed by another property of any primitive type; by-key reads are `/odata/Countries('DE')`. Record attachments, social, workflows, entity history and webhook entity events refuse entities without a Guid `Id` at registration; client entities skip such sets. See [OData](../web/odata.md#keys-other-than-id).
- OData: computed properties (`ODataComputedProperty`) can be selected (`$select=ReferenceNumberDisplay`) and then come back with their value; an expression over the row's own columns works on sets of external databases too. See [OData](../web/odata.md#computed-properties).
- The daily notification digest leaves out notifications that went out as a mail of their own.
- Notifications: types with categories and per-user preferences (`AddNotificationTypes`, scope Off/All/Followed, in the app and by e-mail, `cw.NotificationPreferences`), following entities (`Followable<T>`, `cw.NotificationFollows`, `CoworkeeFollowButton`), `INotifier.PublishAsync(NotificationMessage)` for recipients, followers and an audience, e-mails per notification (template `Notifications.Notification`) with delay and debounce (`cw.PendingNotificationMails`, job `coworkee-notification-mails`), done state, counts per category and search in the inbox (tabs *Inbox*, *Preferences*, *Following* on `/notifications`), new `ICoworkeeApi` members. Apps need a migration. See [realtime](../modules/realtime.md#preferences-following-and-delayed-mails).
- Comments: `Status` (`None`, `Open`, `Closed`) and app `Properties`, `PUT /api/v1/comments/{id}/status` with the new permission `Social.Comments.SetStatus`, `SocialOptions.ValidateComments` (a check before every change), the domain event `CommentChanged` (after the commit). `CoworkeeComments` shows the status, offers a status menu and takes `NewStatus` for new comments; `ISocialApi.SetCommentStatusAsync` and `AddCommentAsync(..., CommentRequest)`. `Comment` is an aggregate root now; apps need a migration. See [social](../modules/social.md#status-app-values-and-checks).
- Exports: `GET /api/v1/data/{set}/export/stream` streams progress lines (NDJSON) and ends with a one-time anonymous download link (five minutes); a client that leaves gets the file by mail. `POST /api/v1/data/{set}/export/mail` mails an export to the user and further recipients. Scheduled exports (`cw.ExportSchedules`, permission `DataTransfer.ExportSchedules`, job `coworkee-export-schedules`): *Schedule export* in the table's export menu, page *Scheduled exports*, `IDataTransferApi` members, mail template `Data.Export`, `IExportMailer`. `ReportSchedule` has `Every` and `Week` (monthly on the last Friday), also for report subscriptions (new columns). Apps need a migration. See [import and export](../web/import-export.md#progress-stream-and-one-time-download).
- Temporary file links: `POST /api/v1/files/{id}/token` and `POST /api/v1/attachments/{type}/{id}/{slot}/{attachmentId}/token` return a token that opens the file without sign-in for five minutes (`/api/v1/files/token/{token}/{name}`, inline or `?download=true`); *Copy temporary link* on the *Files* page, `IFilesApi.CreateTokenAsync`, `IAttachmentsApi.CreateTokenAsync`. `IAccessTokens` (table `cw.AccessTokens`, single use or until expiry, only hashes stored) for own anonymous links. Apps need a migration. See [files](../modules/files.md#temporary-links).
- `CodeEditor.Highlight`: a regular expression whose matches are highlighted; the mail body editor highlights `{{ ... }}`.
- Mail body editor: placeholders are chips showing their name, the visual toolbar has the mail formatting tools and *Insert placeholder*, Ctrl+Space opens the placeholder list in both modes, `{{` works like `{` in Visual. See [mailing](../modules/mailing.md).

### Changed

- OData facets whose value is read through a navigation into a set with row filters (`ValuePath = "Buyer.OrganizationId"`) are built and counted over the targets the user may see; a row whose target is hidden has no value there (as `$expand` shows `null`) and is left out of that group. Before, such facets were left out. Facets by the target's key (`it.Bin.Id`) follow the same rule now and no longer count hidden targets. A target set the user may not read, or whose row filters do more than `Where`, still leaves the facet out. A facet is also left out when its label path or a value path of the row other than its own property reads a member denied to the user, or a label path cannot be checked (an expression). See [OData](../web/odata.md#navigations-follow-their-target).
- A row filter that fails when read outside its set is logged once per type and process instead of on every request.
- The export menu of `CoworkeeDataTable` uses `IDataTransferApi.StreamExportAsync` instead of `ExportAsync`; component tests that stub `ExportAsync` for it stub `StreamExportAsync`. `IDataTransferApi` has the new member `CancelJobAsync`: hand-written implementations and test fakes add it.
- Notifications: `cw.PendingNotificationMails` gets `MailModel`; add a migration. Delayed notification mails render in the tenant that sent them (its template versions) and carry it in the mail log, like immediate ones. The mail layout links `unsubscribe_url` only when it is an absolute http(s) address.
- Turning on *Offered in the registration* (`RoleRequest.SelectableForRegistration`) or *Given to new users* needs `Identity.Users.Manage` and every permission of the role; `Roles.Manage` alone gets 403 (`identity.role_offer_more_permissions`). The same holds for making a group the default, adding roles to the default group and adding grants to a role or group new users get (`Groups.Manage` or `Permissions.Manage` alone).
- `IGroupScope` has two new members, `DefaultGroupIdAsync` and `OwnersAsync`; own implementations and test fakes add them, as they do for the new `ICoworkeeApi` members `SetGroupOwnersAsync`, `GetDefaultGroupIdAsync` and `SetDefaultGroupAsync`. `RoleRequest` and `GroupDto` have new optional parameters (`IsDefault`, `OwnerIds`); `RoleRequest.SelectableForRegistration` is a `bool?` now: a flag left null keeps its value, so the role page sends only the switch it changed. Add a migration: `cw.Groups.IsDefault`, `cw.GroupMembers.IsOwner` and `cw.Roles.IsDefault` are new columns, and the filtered unique index `IX_Groups_TenantId_IsDefault` allows one default group per tenant. Index filters of own model contributors may write booleans as `= TRUE`; on SQL Server they become `= 1`.
- `CodeCompletion.InsertText` is the whole text now, trigger included (`{{ name }}` instead of ` name }}`): a pick replaces what was typed of `CompletionTrigger` and the closing brackets Monaco added, and Ctrl+Space inserts a complete text anywhere. New `Snippet` flag for Monaco snippets (`{{ if $1 }}`). Own completions passed to `CodeEditor` need the trigger in their text.
- Mail body editor: bodies whose markup the visual editor would change (styles other than `color`, `background-color` and `text-align`, tags beyond text formatting, comments) open in Source, so a styled button is no longer lost on save. The shipped templates with a button open in Source.

### Fixed

- The BFF sends `X-Content-Type-Options: nosniff` once also on answers the reverse proxy forwards from the API (they carried it twice); an own `Referrer-Policy` of an answer is kept.
- OData: a value-type member read through a navigation into a set the user may not read (`$filter=Main/Id ne null`) answered `500`; it is `null` now, like the rest of that target.
- Groups page: the Roles tab showed no roles, and picking one removed the group's other roles; *Move all members to…* never showed. The `Groups` OData set serialized `Members` and `Roles` as empty lists. It now carries the real `Roles` and no `Members`, and the Users set its real `Groups`. `GroupRow` (Coworkee.Client.Blazor) has no `Members` any more and `GroupMemberRow` is gone; count members on `Users` with `Groups/any(m: m/GroupId eq {id})`.
- The visual mail editor showed an empty toolbar: `RichTextEditor` passed `Tools="null"`, which MudExRichTextEditor 9.5.5 reads as no tools. It passes `Tools` only when set now.
- Source mode of the mail editor: typing `{{` and picking a placeholder wrote `{{ name }}}}`, because Monaco had closed the braces already.
- Security, OData: row filters and set permissions now hold for navigations too. `$expand` shows only the rows the target set lists for the user (a hidden single navigation is `null`), `$filter` with `any`/`all`/`$count` and `$orderby` through a navigation see only visible rows, also in exports, `query_data`, facet filters, `ODataEntityAccess.FilteredAsync` and the global search. `$expand` into a set without its permission answers `403`; into a type whose `IODataEntityFilter<T>` does more than `Where` (`OrderBy`, `Take`, a new query) `400`. Make such filters plain `Where` calls if their type is reached through navigations. `ODataNavigationScope` applies the rules to own queries. See [OData](../web/odata.md#navigations-follow-their-target).
- Security, reports: fields through a navigation (`Customer/Name`) group and aggregate only over the rows the target set shows the user; a hidden target counts as empty. Before, only the set's permission applied. Table widgets and subscription sheets read them the same way.
- OData: `Hide` and `HideUnless` work for members a base class declares, also when the EDM shares the base type between sets (a navigation points to the base): the member stays in the model and is refused on the hiding set only (`403`), with a warning at startup. Protected properties are checked by the row's own type, so a `HideUnless` on an inherited property holds in lists and expanded rows. See [OData](../web/odata.md#hiding-properties).
- OData `400` answers carry OData's own message in `detail`; other query errors (EF cannot translate the query, which can quote row filters) carry none and are logged.
- Security, OData: the checks of protected properties, `$expand` permissions and navigation filters read the options as OData parses them; a key with a space (`?+$filter=`, `%20$expand=`) slipped past all of them. Parameter aliases (`@a=Size`) and `$apply` through navigations answer `400`. `[AutoExpand]` navigations follow their target (a hidden or forbidden target is `null` or empty).
- Security, OData: facets count on the narrowed rows (a `$filter` through a navigation counted hidden rows), and a facet label read through a navigation (`LabelPath = "it.Customer.Name"`) shows the key for rows the user may not see.
- Security, OData: computed properties with a `Permission` are refused behind navigations (`$expand=Bins($select=Volume)`, `$filter=Featured/Volume gt 0`) and left out of expanded rows; `Hide` on a navigation of a shared base holds on rows reached through the base.
- OData: two filtered reference levels (`Invoice/Shipment/Customer`) and a collection under a hidden reference translate; a row filter that fails on the in-memory query the scope reads it from hides only its own type, and only the navigation targets a query reaches are checked. `ODataNavigationScope.For(services, type)` with `ApplyAsync` replaces `ForAsync` and `Apply`; reports check each target once per query.
- OData: `/odata/<Set>/$count` answered `500`; the model's page size (`[Page]`) applies on entity sets again.
- Global search: fields through a navigation (`t => t.Bin!.Name`) are read in the query with the navigation's rules; a hidden target leaves the field empty. Before, the title read them from the loaded row, where navigations are not loaded.
- Reports on SQLite group `DateTimeOffset` fields of a Coworkee DbContext by date (year, quarter, month, week, day, in UTC); they were rejected. The DbContext stores them as numbers; the buckets are computed from those.
- Security, OData: the checks of protected, hidden and computed properties run on the bound query instead of its text: `Featured/ Size` (a space after `/`) and `$compute=Owner/ Code as C` slipped past them, and literals naming a property (`'a/Note'`) were refused. Export, report filters, facet filters and `query_data` use the same check. `$search` (lists and exports) matches only properties the user may read. Facets whose value is read through a navigation are left out. `$apply` through navigations is refused only into rows with row filters or permissions.
- Security, OData: entity-set aggregates in `$apply` (`aggregate(Employees(Salary with max as M))`) read protected properties past the checks; every member a parsed node holds is checked now, and a query the checks cannot parse answers `400`. `ISearchable` search fields through a navigation follow the target's rules. Facets through a navigation are left out only when the target narrows or the value is denied; keys and targets without rules stay. A member of a shared base hidden on one set stays readable on the others.
- Security, OData: `ISearchable` search fields are checked on every member they read (concatenations, method calls), and left out through navigations into filtered targets; a facet read by the target's key needs the row's own mapped, readable foreign key to EF's key, else it follows the target's rules; facets through a collection are refused at startup; a query node the checks cannot read answers `400`. Reports on SQLite refuse date buckets of an external context's `DateTimeOffset` cleanly.
- `AddODataImport` with an id (update, upsert) refuses a set keyed by something other than a Guid at registration, and updates rows of sets keyed by a Guid other than `Id`. `AddExtendedAttributes` refuses an entity without a Guid `Id`.
- Security, OData: query options without `$` (`filter=`, `orderby=`) are no longer read; they slipped past the checks of protected properties. Clients send `$filter`.

### Security

- Exports: every status change of an export job applies only from its expected status (running to queued, queued to running, running to completed or failed, running or queued to cancelled), also in the stale sweep, which fails jobs one by one. A cancel or the sweep that lands while a streamed export is handed to the queue or finishes wins: the stream ends with `cancelled` or `failed` instead of an error, and nothing is queued, kept or mailed. The export job mails only after its completed status is saved; a failed mail leaves it completed with a message (before, it turned the job failed).
- The per-user export limit covers streamed, background and mailed exports and is checked under a lock per user. Stale exports count from `StartedAt` (new column of `cw.DataTransferJobs`, whose `Status` is now a concurrency token) plus a margin; an export job runs only queued jobs and keeps a failure the sweep wrote meanwhile. Notification mails go only to active addresses; delayed ones keep the newest text when the outbox retries an older one. Apps need a migration.
- Anonymous token links (temporary file and attachment links, export downloads) are rate limited per address (policy `tokens`, 120 per minute), and their tokens are masked (`***`) in the logs of ASP.NET Core, HttpClient and YARP (scopes included) and in the activities of incoming, forwarded and outgoing requests, in the API and the BFF; `Coworkee.Core.Diagnostics.SecretPaths` for own hosts and logs. Coworkee.Bff now references Coworkee.Core. Expired tokens go with the retention target `access-tokens`.
- Exports: streamed exports have a per-user limit (`Coworkee:OData:MaxConcurrentExports`, 429), hand exports above `BackgroundExportRows` to the export job and time out (`Coworkee:OData:ExportTimeout`); stale running exports fail. Scheduled exports send nothing once the owner is deactivated or lost `DataTransfer.ExportSchedules` or the set's permission, and exports of selected groups fail when none of them is accessible instead of listing all.
- Notifications reach followers only while they still see the entity (checked as each follower against the `Followable<TEntity>` set); followers of `Followable(string)` types only through an `Audience`. Waiting notification mails are written through the outbox after the sending command commits, are unique per user, type and entity, are claimed by version and never fail the command. The job `coworkee-stale-exports` fails exports stuck past `ExportTimeout`; without the Retention module expired access tokens are deleted when another token is redeemed.
- Comments: changing a status, also by the author or when reopening, needs `Social.Comments.SetStatus`; authors give a status only when they write a comment.

## 0.0.1.41 (2026-10-27)

### Added

- Group tree and scope: groups have a parent and a `Code` path (create under a parent, move with `PUT /api/v1/identity/groups/{id}/parent`, delete removes the subtree), members can be added, removed and moved, `IGroupScope` (`Coworkee.Application.Groups`) gives filters the accessible, selected and active groups, `<GroupScopePicker />` lets users narrow lists to some groups, `AddGroupDetailTab` adds tabs to the groups page, group import by parent path. New permissions `Identity.Groups.ManageMembers` (implied by `Manage`) and `Identity.Groups.ScopeAll`; see [identity](../features/identity.md#group-tree-and-scope).
- External databases: `AddCoworkeeExternalDbContext<TContext>()` lets OData sets, row filters, search, export, reports and client entities read entities of a second, plain `DbContext`, and the unit of work saves it; client entities take `ChangedAt` date columns for entities without `IAuditable`. See [persistence](../fundamentals/persistence.md#external-databases).
- Users OData set: `Groups` memberships, filter `Groups/any(m: m/GroupId eq {id})`.
- Groups page: Import (`Groups.Manage`) and Export buttons. The `Groups` set has a computed `ParentPath` ("Customer A/Hamburg"), and the export writes `Name`, `Description` and `ParentPath` in tree order, so the file imports again, also into another tenant.
- Computed OData properties with `Exported = true` are export columns; `ComputedQueryRoot` (moved from Workflows) and `ODataComputedProperty.Inline` help to write subqueries in them.
- Log in as another user: permission `Identity.Users.Impersonate`, *Log in as* on the users list and the user page, *Back to my account* in the user menu. `POST /api/v1/identity/users/{id}/impersonation` issues a one-minute ticket, `/bff/impersonate` and `/bff/stop-impersonation` switch the sign-in through the auth server pages `/Account/Impersonate` and `/Account/StopImpersonation`. Tokens carry `impersonator` and `impersonator_name`, `/bff/user` returns `ImpersonatorId` and `ImpersonatorName`. `[NotWhileImpersonating]` refuses a request while impersonating; see [identity](../features/identity.md#log-in-as-another-user).
- `AuthClientOptions.AllowImpersonation` (`Coworkee:Auth:Clients:<n>:AllowImpersonation`, default off): only such clients serve someone signed in as another user, the others answer `access_denied`. `AddWeb` sets it for the web client; set it for own BFF clients that should support *Log in as*.
- `AuthServerOptions.MaxImpersonationDuration` (`Coworkee:Auth:MaxImpersonationDuration`, 8 hours): an impersonation ends at the next authorization or refresh after it. It also ends when the impersonator signs out everywhere, changes his password, is locked, or the rules no longer hold.
- `AuditQuery.ImpersonatorId` (`GET /api/v1/audit?impersonatorId=...`); `ImpersonatedUser`, `JobEnvelope` and `IntegrationMessage` have an optional `ImpersonatorId`.

### Changed

- Apps need a migration (`dotnet ef migrations add GroupTree`): `cw.Groups` gets `ParentId` and `Code`, the name is unique per parent instead of per tenant, new table `cw.GroupSelections`. The migrations host gives existing groups their codes.
- `SetGroupMembers` needs `Identity.Groups.ManageMembers` instead of `Manage`; roles with `Manage` keep working, it implies the new permission.
- `ICoworkeeApi` has new group members (`GetGroupChildrenAsync`, `SearchGroupTreeAsync`, `MoveGroupAsync`, `AddGroupMembersAsync`, `RemoveGroupMembersAsync`, `MoveGroupMembersAsync`, `GetMyGroupsAsync`, `SelectGroupsAsync`): hand-written implementations and test fakes no longer compile until they add them.
- `PUT /api/v1/identity/groups/{id}` with a `ParentId` other than the current parent answers 400; a group moves with `PUT /api/v1/identity/groups/{id}/parent`. A missing `ParentId` keeps the parent.
- `IUnitOfWork` resolves to a wrapper that saves the external contexts and then the app's context, no longer to the DbContext itself; code that cast it to a DbContext injects the context instead.
- Aspire 13.6.4 (app host SDK, `Aspire.Hosting.*`, `Aspire.Hosting.Testing`), CommunityToolkit Aspire packages 13.6.1, Nextended 10.1.48; `coworkee update` moves the pinned packages.
- Apps need a migration (`dotnet ef migrations add Impersonation`): `cw.AuditEntries` and `cw.OutboxMessages` get the nullable column `ImpersonatorId`.
- `ICoworkeeApi` has the new member `StartImpersonationAsync`, `ICurrentUser` the default member `ImpersonatorId` (null): hand-written implementations of `ICoworkeeApi` and test fakes no longer compile until they add it; own `ICurrentUser` implementations that read claims should return the `impersonator` claim.
- While someone is signed in as another user, the account and consent pages of the auth server answer 403, and changing the own e-mail address, creating API keys, exporting personal data, deleting the own account, choosing the active role and creating or changing webhooks and report subscriptions are refused (`auth.impersonating`).
- `IsLocalPath` of the BFF (`/bff/login`, `/bff/impersonate`, `/bff/stop-impersonation`) refuses return addresses with control characters or backslashes.
- `[Realtime]` still broadcasts to everyone with the view permission; with groups as customers in one tenant, clients of other units see record ids and changed property names. Per-group topics follow.

## 0.0.1.40 (2026-10-25)

### Fixed

- Kibana: the sign-in ended with 500 at `/oauth2/callback` ("id token issued by a different provider"); oauth2-proxy expects the issuer with the trailing slash the auth server puts into `iss`.

## 0.0.1.39 (2026-10-25)

### Added

- Open WebUI: `app.AddOpenWebUI(ollama)` adds a chat over the models of Ollama, signed in at the auth server like the other tools; template option `openWebUI` (`--open-web-ui`, with `--ai-provider ollama`), a card in the Studio and part of the preset *Everything*; deploy tool `openwebui`.

### Changed

- Grafana, WebDataStudio and AspireUI sign in at the app's auth server, with or without Keycloak, like Kibana's proxy: one client `{app}-{tool}` each, administrators of the system organisation only, as admins of the tool. The local administrator login stays. They no longer get clients of the Keycloak realm.
- Auth server: user info endpoint `/connect/userinfo` (sub, email, name, role, tenant). `Coworkee:Auth:DynamicBackchannel` names sign-in and sign-out by the issuer and token, user info and keys by the address asked, so tools in containers reach it over the container network while the browser uses the host address; the app host sets it with the issuer in a local run.
- Grafana 13.2.4 (Nextended 10.1.47); data sources and volumes of 11.5 keep working. **Action:** an app host that pins `Nextended.Aspire.Hosting.AspireUI` (`aspireUI` option) raises the pin to 10.1.47, or restore fails with NU1605.
- Studio: cards of options that do not matter right now stay visible, disabled, with the reason as tooltip; Kibana sits right after Search and is part of the preset *Everything*.

### Fixed

- Auth server: clients that ask for `response_mode=form_post` (ASP.NET Core's OpenID Connect handler, WebDataStudio) got a blank page after sign-in; the content security policy blocked the script that sends the answer on. It allows that one script by its hash now.
- Kibana: the sign-in of a local run sends the browser to the host addresses of the auth server and the proxy; it went to `aspire.dev.internal`, which only containers know. The dashboard no longer links `kibana-server`, which let everyone in without the proxy.
- AspireUI: a local run gives it the volume `{app}-aspireui-data` like a deployment. The shared `aspireui-data-aspireui` let the app started last set the Keycloak sign-in of every other app (404 on `/realms/<other app>/.well-known/openid-configuration`). The old volume stays; delete it with `docker volume rm aspireui-data-aspireui` once nothing uses it.

## 0.0.1.38 (2026-10-25)

### New

- Themes: six new built-in themes, 21 in total, which also differ in where the logo sits: GitHub (dark header with the logo, no logo in the menu), LinkHub (pill buttons through custom CSS, logo in the menu only), Terminal (no logo at all), Slate (dark sidebar with the logo, plain header), Ember and Mint (logo in the header only). Built-in themes can now bring custom CSS, app themes take it from their base. CLI/template `--theme`, Studio cards. See [Built-in themes](../modules/theming.md#built-in-themes).
- Kibana: `options.UseKibana()` puts Kibana next to the Elasticsearch container (template and CLI option `kibana`, Studio card *Kibana*, deploy tool `kibana`). oauth2-proxy in front of it signs in at the app's auth server, with Keycloak when it is on; Kibana's own single sign-on would need a paid Elastic license. New `AuthClientOptions.SystemAdministratorsOnly`: the auth server lets only administrators of the system organisation sign in at such a client. See [Kibana](../hosting/aspire.md#kibana).
- Backups: upload a downloaded zip again (*Upload backup* on `/admin/backups` and in the AirWorks backups app, `POST /api/v1/backups/upload?name=`, `UploadBackupCommand`); it is listed like a backup made here and restored the same way. See [Database backups](../modules/backups.md).

### Fixes

- Reports list: the favourite star and the menu of a report card sit on one line.

## 0.0.1.37 (2026-10-20)

### Breaking changes

- **Migration needed** in apps with `Coworkee.Reporting` (the template has it by default): the new tables `cw.ReportSubscriptions` (report subscriptions) and `cw.OutgoingMailAttachments` (Mailing, files of queued mails). `CoworkeeReportingModule` now depends on `CoworkeeMailingModule`.
- `DateRanges` moved from `Coworkee.Client.Blazor.Reporting.State` to `Coworkee.Contracts.Reporting` (the server uses it too); add the `using` where it is called directly.
- **MudBlazor.Extensions 9.11.6**: apps that pin MudBlazor.Extensions themselves move to 9.11.6.

### New

- Reporting: subscriptions by mail. *Subscribe by mail* on a report mails it daily, weekly or monthly at a time of a time zone, with an own date range and facets if wanted; the mail (`Reports.Subscription`, all languages) in the subscriber's language (else the default language of the organisation) shows KPIs with their threshold colours and the first rows of the other widgets (the latest buckets of a time series), an Excel workbook holds all rows, a link ends the subscription. The job `coworkee-report-subscriptions` runs each one as the subscriber, so their permissions and row filters apply. *Reports › Subscriptions* lists them; the new permission `Reports.Subscriptions` shows those of the organisation. Mailing: attachments (`MailAttachment`, `IMailSender.QueueWithAttachmentsAsync`, `MailEnvelope.Files`) for all four providers; own `IMailTransport`s that ignore `Files` keep working without them. See [Subscriptions by mail](../modules/reporting.md#subscriptions-by-mail).
- Reporting: create and change reports with AI. *Create with AI* on the report list and *Ask AI to change this report* in the designer send a prompt to `POST /api/v1/reports/generate` (`GenerateReportDefinitionQuery`, `Reports.Manage`, needs an AI model): the server builds the instructions from the sources the user may read, checks the answer like a save and asks again with the errors (at most twice); the definition opens unsaved in the designer (`reports/new` for a new one, undo for a change). New assistant/MCP tool `generate_report`; `create_report` and `update_report` are offered to the assistant now (they run after the user confirmed). See [Create a report with AI](../modules/reporting.md#create-a-report-with-ai).
- Reporting: thresholds. `WidgetOptions.Thresholds` (`ThresholdStop`: start value, theme colour `success`/`warning`/`error`/`info` or `#rrggbb`, label) colour KPI values and gauges, pivot cells, the table column `ThresholdColumn` and, with `ColorBarsByThreshold`, bars; every band also shows an icon and its label. The designer has a band editor with *Bands from the target*. See [Thresholds](../modules/reporting.md#thresholds).
- Reporting: switches for viewers. `WidgetOptions.Metrics` (`MetricView`: label, aggregates, number format, extra filter) and `WidgetOptions.Buckets` (`BucketView`: label, group-by field, date bucket, date field) offer alternatives that viewers pick in the widget header of KPIs, charts, pivots and tables; the report is not changed, exports use the choice, and the choice is kept per user in the new user setting `Reports.Views`. Saving checks every alternative. See [Switches for viewers](../modules/reporting.md#switches-for-viewers).
- AirWorks: full-screen app launcher. `Coworkee:AirWorks:Launcher` of the AirWorks host (`menu`, the default, or `overlay`) reaches the desktop as `launcher` of `/bff/user`; the template writes it to the host's `appsettings.json` (option `airworksLauncher`, CLI `--airworks-launcher`, Studio card *AirWorks app launcher*). The launcher lists the administration apps under *Administration* and other apps under their `launcherGroup` (new on `CoworkeeApp`, `entityApp` and `embedApp`, which also takes `shortcut`; `group` stays the section of the administration), with a context menu (*Open*, *Add to desktop*) and a focus trap. Existing AirWorks clients move `@fgilde/airworks` to commit 9657ff3 (AirWorks 0.6.0) in `ClientApp/package.json` to use it. See [App launcher](../web/airworks.md#app-launcher).

## 0.0.1.36 (2026-10-20)

### Breaking changes

- **MudBlazor.Extensions 9.11.5**: apps that pin MudBlazor.Extensions themselves move to 9.11.5.

### Fixes

- Navigation: the filter text typed after the filter chips stays in the filter box when a page is opened; it was emptied while the filter still applied. The fix comes with MudBlazor.Extensions 9.11.5.

## 0.0.1.35 (2026-10-20)

### Breaking changes

- Record attachments: `IRecordAttachments.PutAsync`, `DeleteAsync` and `DeleteAllAsync` no longer save; they join the caller's unit of work (the pipeline saves after a command, other code calls `IUnitOfWork.SaveChangesAsync`). Blobs written for rows that are never committed are deleted at the end of the scope, blobs of removed rows after the commit. App code that called them outside a command saves itself now; reads (`OpenAsync`, `ListAsync`) see saved rows only. The HTTP endpoints save as before. See [Record attachments](../modules/attachments.md#in-app-code).
- **Migration needed** in apps with `Coworkee.Social`: the new table `cw.Reactions` (emoji reactions).
- `Error.Validation(field, message).Message` is the given message instead of *One or more validation errors occurred.*; so is the `title` of the 400 problem details. `code` (`validation`) and `errors` are unchanged; the dictionary overload keeps the generic text.
- **Migration needed** in apps with `Coworkee.Workflows`: the new column `RequestedBy` of `cw.WorkflowApprovals` (who ran the transition into the approval). The escalation of an approval with `NotByRequester()` no longer adds the requester as an approver when they are in the escalation role; approvals started before the migration have no requester stored and are not filtered.

### Fixes

- `coworkee entity add`/`module add`: generated list pages show bool columns as translated *Yes*/*No*, enum columns by their translated label (`EnumLabels`), `DateTimeOffset` columns in local time and `DateOnly` columns as short dates in the user's culture, instead of *True*, member names and `+00:00` offsets. Existing generated pages can take the same `CellTemplate`s.
- Localization: 57 library texts that showed in English in every language now ship in all 22 languages: the setup wizard (labels, steps, hints, messages), the descriptions of the Reports, Chat and report data pages, *No reports yet*, *Chat with {0}*, the permission matrix and resource permissions (*Search permissions*, *User or group*, ...), version history, audit timeline, theme picker, *My settings* and the navigation pins. The literal-key test also checks `l[...]`, both texts of `L[a ? "…" : "…"]` and the texts passed to `PageHeader` and `CoworkeeDataTable`.
- Reporting: report names and descriptions, widget titles and value labels show their translation when the app has a text for them (seeded reports can be localized; the stored text stays the key, `CoworkeeLocalizer.Stored`); a widget added from a preset stores the preset's key instead of the translated title. The donut total and the target line are translated, and category labels of the x axis turn by 45° instead of being cut ("2…", "Back…"). See [Reporting](../modules/reporting.md#seeding-a-report-on-a-source).
- `CoworkeeDataTable`: column headers keep their label on one line, so a wide table scrolls sideways instead of squeezing its last columns ("Kostenstell", "Stat") under the sticky action column.
- Layout: the app title in the app bar stays on one line and ends with an ellipsis on narrow screens instead of wrapping.
- `CoworkeeWorkflow`: the due date of an approval keeps its space after the waiting text ("…listed. Due 25.10.").
- Aspire: `AddMigrations` wires the modules the migrations project references like any service (storage, mail, Redis, search, translation, `app.Modules` entries); files a seed stored went to the local folder of the migrations host before, and opened with 404 in the app. App hosts that wired `CoworkeeModules.Storage` into the migrations by hand can drop that line.
- Dialogs: a side sheet or dialog of the library opened from inside a dialog the app showed with the plain MudBlazor `ShowAsync` (for example the file preview of `RecordAttachment`) stayed invisible; MudBlazor.Extensions applied its options to the outer dialog. The layout now marks dialogs that are already open when a new one is added.
- Reporting: charts over a computed source (`AddReportSource`) with several rows per category and series no longer fail (the in-memory grouping split every row into its own group, the chart then threw on the duplicate keys); chart series add up duplicate categories.
- Record attachments: scopes created with `CreateScope()` (start-up code, the Elsa module, older services) no longer fail to dispose once the attachments module is on (`AttachmentBlobs` is `IDisposable` too).
- Security, social: comments, tags, ratings, reactions and their realtime topics of a type registered with its entity class (`Comments<T>`, ...) now require that the user sees the row through the type's OData set (set permission, organisation, soft delete, `IODataEntityFilter<T>` row filters); before, the permission and an existing id were enough. Tag counts and `GET /api/v1/tags/{entityType}/entities` only count visible rows. Types without an OData set keep the id check; `Authorize(...)` still adds checks on top, and hooks that only repeated the row filter can go. See [Social](../modules/social.md).
- Workflows: for an entity the command added in the same unit of work, `WorkflowEngine.LoadAsync` loads the `Include(...)` references through their foreign key when only the id was set, so `Describe`, guards and actions no longer see `null` there.
- `CoworkeeDataTable`: the sticky action column stays opaque on hovered and striped rows (the translucent row tint is painted over the surface), so long cell text no longer shows through under the buttons, in light and dark mode.
- `coworkee entity add`/`module add`: navigation entries, page titles and permission groups show names of several words as words ("Benefit enrollments", "Time off: Benefit enrollments") instead of the PascalCase plural; dialog titles and permission names already did.
- `coworkee entity add`/`module add`: generated pages pass text keys to `PageHeader` and `CreateText` instead of translated texts, which were translated a second time ("People" → "Personal" → "Persönlich"). Rule: `PageHeader` and `CoworkeeDataTable.CreateText` translate, callers pass keys. Generated pages of existing apps drop the `@L[...]` around these values.
- `coworkee entity add`/`module add`: reference facets get `LabelPath = "it.{Navigation}.{Display}"`, so a navigation named like a Dynamic LINQ keyword (`Parent`, `It`, `Root`, ...) no longer makes the OData set answer 500 when facets are asked for. Hand-written facets on such a navigation add the `it.` themselves. See [OData](../web/odata.md#facets).
- `ResourceCalendar`: a month fits the available width (day columns shrink, narrower resource column) and fades at the right edge when it scrolls; rows grow to a two-line resource cell instead of clipping it; a row with a `UserId` shows the avatar with presence plus `Title` and `Subtitle` instead of the user card without the subtitle.
- Security, workflows: the state, history, comments, transitions, decisions and realtime topic of a workflow now require that the user sees the row through its OData set (set permission, organisation, `IODataEntityFilter<T>` row filters); before, `Workflows.View` and an id were enough. Approvers of the row's open approval still see and decide it. `WorkflowEngine.LoadAsync` takes an optional `WorkflowAccess` (default `See`). See [Workflows](../modules/workflows.md#permissions).
- Template: `App.razor` links the scoped CSS bundle, so `.razor.css` files of the web client apply out of the box (the home page logo uses one). Existing apps whose web client has `.razor.css` files add `<link href="@Assets["MyApp.Web.styles.css"]" rel="stylesheet" />` (their web project name) after the MudBlazor.Extensions stylesheet.
- API tests of an app with `--event-bus rabbitMq` start without a broker: new setting `Coworkee:EventBus:RabbitMq:Enabled` (default `true`); the template fixture sets it to `false`, so events reach the handlers in process. Existing apps add `builder.UseSetting("Coworkee:EventBus:RabbitMq:Enabled", "false");` to their `ApiFactory`. See [Testing](../testing.md).
- `coworkee spec validate` refuses an entity whose plural equals its name (the generated feature namespace hid the entity type and the app did not build).

### New

- Record attachments: realtime topic `attachments:{entityType}:{entityId}` (`AttachmentTopics.Record`) on every added, replaced or deleted attachment, for users who may read a slot of the record; `RecordAttachment` follows it and reloads. Personal data export section `attachments` (the user's uploads, metadata only; erasing keeps the files like the `files` section). See [Record attachments](../modules/attachments.md#realtime-and-personal-data).
- Workflows: `State(...).Approval(ctx => ..., rejected, a => a.ApprovedTo(...))` chooses the approved (and with a second function the rejected) state from the entity when the approval is decided, e.g. a second approval for large amounts behind one submit transition; the possible states are listed with `ApprovedTo`/`RejectedTo` and checked at start-up and at every decision. `ApprovalStep.ApprovedState`/`RejectedState` are `null` for such approvals; `ApprovedTargets`/`RejectedTargets` list the states. See [Workflows](../modules/workflows.md#approvals).
- Blazor: `CoworkeeDetailPanel<TKey>`, the details of a board or list item in a right drawer (full screen on phones) bound to a query parameter (`?item={id}`, `QueryParameter`): links, reload and the back button open and close it; Escape and the close button close it; `Title`, `Actions` and `ChildContent` fragments get the key, `Width`, `KeyChanged`, `Open(key)`. See [Detail panel](../ui/detail-panel.md).
- Record attachments (`Coworkee.Files`, module `CoworkeeAttachmentsModule`, **migration needed** for the table `cw.RecordAttachments`): `services.AddRecordAttachments<LeaveRequest>("LeaveRequests", a => a.Slot("certificate", maxBytes, "application/pdf", "image/*").MultiSlot("documents").WriteRequires(permission))`; endpoints `/api/v1/attachments/{type}/{id}/{slot}` (list, inline or download, upload, delete) whose reads follow the row visibility of the type's OData set, writes also the write permission and `Authorize` checks; `IRecordAttachments` (`PutAsync`/`OpenAsync`/`DeleteAsync`/`DeleteAllAsync`/`ListAsync`) for app code, `IAttachmentScanner` hook (virus scan), retention target `orphaned-attachments`. Client: `IAttachmentsApi` and the `RecordAttachment` component (one slot: upload with `MudExUploadEdit`, view with `MudExFileDisplay`, delete). `ODataEntityAccess.VisibleIdsAsync<T>`: the ids the current user sees. See [Record attachments](../modules/attachments.md).
- Anonymous records: `IAnonymous` (or the base `AnonymousEntity` with random `Guid.NewGuid()` ids) for rows that must not point to their author: no `CreatedBy`/`ModifiedBy`/`DeletedBy`, no audit trail entry, snapshots without `CreatedBy`, outbox/webhook/Elsa trigger messages without user and trace id, `CreatedAt`/`ModifiedAt`/`DeletedAt` cut to the UTC day (`IAnonymous.TimestampPrecision`, `record.Truncate(time)` for own timestamps). See [Auditing](../modules/auditing.md#anonymous-records).
- OData: `services.AddODataChildFilter<TalkItem, Talk>(i => i.Talk)` makes child rows follow the row filters of their parent (visible when `ODataEntityAccess.VisibleAsync<Talk>` contains the parent; subquery on the foreign key), instead of a hand-written filter repeating the parent's. See [OData](../web/odata.md#child-rows-follow-their-parent).
- Blazor: small charts without the Reporting module in `Coworkee.Client.Blazor.Components.Charts`: `CoworkeeBarChart` (horizontal or `Vertical`, values, colours, `ShowPercent`, `Max`), `CoworkeeDonut` (segments, centre label, legend) and `CoworkeeSparkline`; plain HTML/SVG in the theme colours (dark mode), texts readable by screen readers. See [Small charts](../ui/charts.md).
- iCalendar files: `IcsCalendar`/`IcsEvent`/`IcsTime` in `Coworkee.Application.Calendar` (all-day, UTC or `TZID` times, location, URL, status, categories, organizer, attendees; RFC 5545 escaping and UTF-8 safe folding at 75 octets) and `Results.Ics(calendar, fileName?)` in `Coworkee.AspNetCore.Http` (`text/calendar; charset=utf-8`). See [Calendar feeds](../web/calendar-feeds.md) for subscription addresses with a token and caching headers.
- Reporting: own data sources. `services.AddReportSource<HeadcountRow>("HeadcountByMonth", "Headcount by month", permission, async (context, ct) => rows)` registers rows of a plain type (an `IQueryable` of the database or a list, aggregated in memory) that reports list after the entity sets (`ReportSourceDto.Computed`, sum icon in the designer) and aggregate like a set: OData filter parsed against the row type, group by with date buckets, all functions; `ReportSourceContext` carries the request services and the date range of the widget's filter (`DateFrom`/`DateTo`). Tables read their rows from `POST /api/v1/reports/rows` (`IReportsApi.RowsAsync`, new interface member); no data table widget or facets for them, drill-down only when `ReportingOptions.DrillDown` names a page. See [Reporting](../modules/reporting.md#own-data-sources).
- AirWorks (`@coworkee/airworks`): the report viewer and designer handle computed report sources: tables on them read `POST /api/v1/reports/rows`, data tables skip them (the designer offers only entity sets there), the source picker marks them with ∑, the date range of a date-only field filters by plain days. `@coworkee/client` has `ReportSourceDto.computed`, `ReportRowsRequest` and `ReportRowsResult`.
- Social: emoji reactions. `AddCoworkeeSocial(s => s.Reactions<Product>("Products").CommentReactions())`, permissions `Social.Reactions.View`/`.Create`, `GET/POST /api/v1/reactions/{entityType}/{entityId}` (toggle `{ "emoji" }`), realtime topic `reactions:{entityType}:{entityId}`, component `<CoworkeeReactions EntityType="Products" EntityId="id" />` (counts, own ones filled, who reacted on hover, picker of `SocialReactions.Defaults`); with `CommentReactions()` every comment of `CoworkeeComments` gets the same row (`CommentDto.Reactions`, `CommentThreadDto.ShowReactions/CanReact`). Personal data export and erase include reactions. `ISocialApi` has `GetReactionsAsync`/`ToggleReactionAsync`. See [Comments, tags, ratings and reactions](../modules/social.md#reactions).
- Workflows: `WorkflowEngine.LoadAsync` finds an entity the current unit of work added and has not saved yet in the change tracker, so a command adds a record and runs its first transition in one `SaveChanges` (no early save; a refused transition saves nothing). See [Workflows](../modules/workflows.md).
- Background jobs: `TenantJobRunner.ForEachTenantAsync((services, tenantId, ct) => ..., ct)` runs work once per active organisation as its system and in an own scope, logs and returns failed organisations and goes on with the others; `ForTenantsAsync(ids, ...)` for the organisations a query found. `ITenantDirectory.GetActiveTenantIdsAsync`. The `workflow-deadlines` job uses it (an error in one organisation no longer stops the run). See [Background jobs](../modules/background-jobs.md).
- Client: `ClientUser.From(principal)` (`IsAuthenticated`, `UserId`, `TenantId`, `Email`, `DisplayName`) and the scoped `CurrentUserInfo` (`GetAsync()`, `Changed`) registered by `AddCoworkeeClient`; the library components use it instead of parsing `sub`, and the client principal carries the `tenant_id` claim. See [Client](../ui/client.md#current-user).
- Permissions: computed permissions grant a defined permission from data (`services.AddComputedPermission(HrPermissions.LeadsPeople, (c, ct) => db.Employees.AnyAsync(e => e.LeaderUserId == c.UserId, ct))` or `IComputedPermission`), evaluated once per request and user after the role grants; they count for `[RequiresPermission]`, `IPermissionChecker`, OData, MCP and the client's permission list (navigation, `PermissionGate`, policies). `CoworkeeNavItem(..., Visible: sp => ...)` hides an entry from a client-side callback. See [Permissions](../security/permissions.md#computed-permissions).
- `ODataPicker`/`OptionalODataPicker`: `TextProperty="FirstName,LastName"` shows several properties joined with spaces, `$select`s and sorts by them and finds a row when every word of the search appears in one of them; with one property a search of several words now also matches when the words are not adjacent. See [Data tables](../ui/data-table.md#edit-dialogs).
- `ResourceCalendar`: `CalendarResource.WorkDays` overrides the work days for one row (part-time people), and unmatched attributes (`id`, `data-testid`, ...) reach the root element. See [Resource calendar](../ui/resource-calendar.md).
- Workflows: `EntityWorkflow<T>.Include(e => e.Employee)` loads a navigation with the entity, so `Describe`, guards and actions can use it (`Describe(l => $"Vacation of {l.Employee.Name}")`). See [Workflows](../modules/workflows.md#states-and-transitions).
- OData sets: `AddODataEntity<T>(set, permission, o => o.Hide(...).HideUnless(e => e.Salary, permission))` shows a property only to users with the permission; lists, single reads, `$filter`/`$orderby`/`$select`/`$expand`, facets, exports, reports, the global search, the assistant's data tools, the entity history and webhooks respect it. `ODataEntityRegistration.ProtectedProperties`, `ODataEntityAccess.DeniedPropertiesAsync`. See [OData](../web/odata.md#properties-behind-a-permission).
- `ISystemDispatcher.SendAsync(request, permissions, ct)` runs a request as the system in the caller's organisation, with exactly the given permissions and in its own scope (audit: *system*); `CurrentUserScope.BeginSystem(tenantId, permissions)` and `SystemActor` underneath. `AnonymousCurrentUser` and `TestCurrentUser` now pass on the ambient `ClientId`/`ClientPermissions`. See [Permissions](../security/permissions.md#acting-as-the-system).
- `CoworkeeDataTable`: `RowHref` (`Func<T, string?>`) and `OnRowClick` (`EventCallback<T>`) open a row by clicking it (not its buttons or checkbox) or by its open button in the action column (keyboard, real link for `RowHref`). See [Data tables](../ui/data-table.md#row-links).
- `ResourceCalendar` (Blazor): resources against days with bars, half days, lanes, holidays and drag to select days. See [Resource calendar](../ui/resource-calendar.md).
- Workflows: `ApprovalStep.UsersFrom(context => ...)` finds approvers from the entity when the approval starts, e.g. the manager of the requester.

### Fixes

- A stored secret setting that the current Data Protection key ring cannot decrypt (for example after the key ring changed) reads as unset with a warning in the log, instead of failing every read of the settings (`/api/v1/ai/status` answered 500 on every page). Save the secret again. `SettingProtector` takes an `ILogger<SettingProtector>`.

## 0.0.1.34 (2026-10-18)

### Breaking changes

- **Migration needed**: passkeys live in the new table `cw.UserPasskeys` (ASP.NET Core Identity schema version 3: key `CredentialId`, `Data` as JSON, cascade from `cw.Users`). Own `ICoworkeeApi` implementations add `ResetUserPasskeysAsync`.
- **Migration needed** in apps that add the new module `Coworkee.Retention`: the table `cw.RetentionRuns`. Apps without it need nothing.
- `SettingWriter` and `SettingProtector` of `Coworkee.Settings` are public now (for modules that write their own settings).

### New

- Passkeys (WebAuthn) on the auth server: *Sign in with a passkey* and the browser's autofill on the sign-in page, the account page *Passkeys* to add (at most 20), rename and remove them, *Passkeys (n)* in the security tab of the profile, *Passkeys: n* with *Remove all* in the admin sign-in panel (`POST /api/v1/identity/users/{id}/passkeys/reset`), `PasskeyCount` in `ProfileDto` and `UserDetailDto`, passkeys in the personal data export. A passkey sign-in skips two-step verification. The setting `Account.Passkeys.Enabled` (default on) and the login mode *External* turn them off; `Coworkee:Auth:Passkeys` binds `IdentityPasskeyOptions` (`ServerDomain` behind a proxy without forwarded headers). See [Passkeys](../security/passkeys.md).
- The audit trail writes binary keys as base64url in `EntityId` instead of `System.Byte[]`.
- `Coworkee.Retention`: days to keep per kind of data (audit trail, snapshots, processed outbox and inbox, soft deleted records, AI tool calls and conversations, webhook deliveries, mail log, notifications, import and export jobs, deleted files with their blobs, backups, OpenIddict tokens), the nightly job `coworkee-retention` (`Coworkee:Retention:Cron`, default `41 2 * * *`), the admin page *Retention* (`/admin/retention`, permission `Retention.Manage`, system organisation only) with live counts and *Delete now*, endpoints `/api/v1/retention` and a section in the system status. Every kind of data that was kept forever stays so until days are set; only the webhook log keeps its 30 days. Modules and apps add their own with `IRetentionTarget` and `services.AddRetentionTarget<T>()`; `ExecuteDeleteInBatchesAsync` deletes in batches of 1000. Template, CLI and Studio option `retention` (on); new apps keep processed messages and tokens 14 days and import and export jobs 30. See [Retention](../modules/retention.md).
- Webhooks: `WebhookOptions.LogRetentionDays` is the default of `Retention.webhook-deliveries.Days`; with the Retention module the old daily webhook log cleanup does nothing.
- `CoworkeeMigrationsOptions.AfterSeed` runs the app's own data steps after the migration and the seed, before Keycloak is reconciled.

## 0.0.1.33 (2026-10-17)

### Breaking changes

- **Migration needed**: a filtered unique index `IX_Themes_Name` on `cw.Themes` (built-in themes, `TenantId` null) and the column `cw.ChatAttachments.ThumbnailKey`. The index keeps two app instances from seeding the built-in themes twice on SQL Server and SQLite (Postgres already refused it); the seed no longer cleans up such duplicates. If the migration reports a duplicate key, delete the extra built-in row first.
- **History restore checks its preview**: `EntityHistoryPreviewDto.Version` and `EntityHistoryRestoreRequest.Version` are new; `POST /api/v1/history/{set}/{id}/restore` answers `409` when the version is missing or the record changed since the preview. The history dialog, the audit log undo and the AirWorks record window preview again. Own `IEntityHistoryApi` implementations and direct API calls pass the version; regenerate OpenAPI snapshots and SDKs. See [Auditing](../modules/auditing.md#record-history-and-restore).
- **`Coworkee.Chat` depends on `Coworkee.BackgroundJobs`** and brings SkiaSharp (MIT) with its native Linux assets for the thumbnails.

### New

- `coworkee update` inserts single lines itself (rule kind `insertions` in `upgrade-rules.json`: after the first line matching a pattern, with its indentation, skipped where the line is already there; `--dry-run` only lists them). The step of 0.0.1.32 (`builder.ConfigureContainer(new DefaultServiceProviderFactory());` in `src/*.Migrations/Program.cs`) is applied automatically now.
- New apps have an `AGENTS.md` with the rules for coding agents (layout, required patterns, pitfalls, migrations, tests, CLI and MCP) and a `CLAUDE.md` that imports it. Existing apps can copy them from a new app of the same options.
- AirWorks: the theme and mail template editors of the administration are the pages of the web app in a window when the BFF knows the web app (`embedOr`), the TypeScript editors stay the fallback; *Workflow designer* opens Elsa Studio in a browser tab (`Workflows.Elsa.Manage`); the record window has *Open in the web app* (the page of the record's global search hit). New text key `Open in the web app`.
- `Coworkee:Auth:FrameAncestors`: origins that may frame the account pages of the auth server (default none), so an AirWorks window can show the sign-in page of an embedded web page. Same site only; see [AirWorks](../web/airworks.md#pages-of-the-web-app-embedapp).
- `app.UseCoworkeeSecurityHeaders()` (`Coworkee.Bff`) sends a Content-Security-Policy for the Blazor WebAssembly client (frame-ancestors from `Coworkee:Bff:FrameAncestors`, MudEx file previews from blob and data URLs, hot reload in Development), `nosniff` and a Referrer-Policy; extra sources per directive or an own policy through `Coworkee:Bff:SecurityHeaders`. The template's web host uses it; existing apps add it before `UseStaticFiles` and drop a hand-written policy. Apps with the cloud pickers of the Files module add their hosts. See [Security headers of the web app](../security/authentication.md#security-headers-of-the-web-app).
- `Coworkee:Bff:MaxRequestBodySize` caps what the web host forwards to the API; by default its forwarded routes have no limit and the API checks its own. A hand-written `ConfigureKestrel(k => k.Limits.MaxRequestBodySize = ...)` in the web or AirWorks host is not needed and can go.
- `builder.AddCoworkeeMigrations<TDbContext>(...)` and `host.RunCoworkeeMigrationsAsync()` (`Coworkee.Identity.Setup`) are the migrations host: migrate with retries (`Migrations:Attempts`), seed the development users or the first administrator of a deployment, keep the development users in step, reconcile Keycloak, and the container fix of 0.0.1.32. The template uses it; existing apps replace their hand-written `src/*.Migrations/Program.cs` with it, so later fixes come with the package (`coworkee update` points at it). See [Migrations and seeding](../fundamentals/persistence.md#migrations-and-seeding).
- Chat pictures get a small WebP the server makes (at most 480 px, turned upright, `GET /api/v1/chat/attachments/{id}/thumbnail`, `ChatAttachmentDto.HasThumbnail`); the message list shows it, a click opens the full file.
- Every chat file opens in the MudEx file viewer (`MudExFileDisplay`), also an upload before it is sent. The rule for own pages is on [Files](../modules/files.md#showing-files).
- The policy of `UseCoworkeeSecurityHeaders` allows the CDNs the file viewer loads its viewers from (`Coworkee:Bff:SecurityHeaders:FileViewerCdns`: jsDelivr, esm.sh, unpkg) in `script-src`, `style-src` and `connect-src`, so Word, Excel, maps and the other viewers work out of the box; clear the list for a policy without third-party script. See [Security headers of the web app](../security/authentication.md#security-headers-of-the-web-app).
- Files uploaded to a chat but never sent are deleted after `Coworkee:Chat:UnsentAttachmentLifetime` (default 24 hours, an hourly job).

### Fixes

- The SDKs page described an `sdk/openapi.json` with a snapshot test and an `sdk/typescript` client that the template never had; it now shows them as a recipe for apps that need them.
- `npm run build` of `@coworkee/client` and `@coworkee/airworks` empties `dist` first, so packs no longer carry files of removed sources.
- The built-in themes Coworkee, Forest and Sunset meet WCAG AA on button text in light mode (slightly darker primary or secondary color); the contrast test covers every built-in theme.
- The file viewer never hands a private file to the online Office viewer of Microsoft.
- Files forwarded by the web host keep their own sandbox policy: `UseCoworkeeSecurityHeaders` adds the app policy only to answers without one (before, forwarded files carried both).
- The content endpoints of files, public files, chat files and thumbnails answer `HEAD`, which the image viewer sends first (it got 405).


## 0.0.1.32 (2026-10-17)

### Fixes

- The migrations of a local run crashed at start since 0.0.1.31: they run as Development, whose service validation fails on the MVC services of OData in a host without a web environment. The template builds the container of the migrations with the production defaults; existing apps add `builder.ConfigureContainer(new DefaultServiceProviderFactory());` after `Host.CreateApplicationBuilder(args)` in `src/*.Migrations/Program.cs` (`coworkee update` points at it).

## 0.0.1.31 (2026-10-17)

### Breaking changes

- **Migration needed** (apps with Chat): direct and group chats bring the tables `cw.ChatConversations`, `cw.ChatMembers`, `cw.ChatReactions`, `cw.ChatAttachments` and new columns of `cw.ChatMessages`. Earlier direct messages move into conversations at the next start; what was read stays read.
- **`Coworkee.Chat` depends on `Coworkee.Storage`** (attachments) and references `Coworkee.Notifications` (mention notifications when that module is there).
- **Chat contracts**: `ChatMessageDto.ToUserId` is nullable (empty in groups) and the DTO has new properties (`ConversationId`, `Html`, `Source`, `EditedAt`, `Deleted`, `ReplyTo`, `Reactions`, `Attachments`, `HasEntityMentions`); `ChatReadDto` has `ConversationId`. `IChatApi` gained the conversation members; own implementations add them. New endpoints under `/api/v1/chat/chats`, `/messages` and `/attachments`: regenerate OpenAPI snapshots and SDKs. The plain text endpoints by person stay.
- **`UserAvatar` opens a menu with *Chat* and *Profile*** for other people when the user may chat (`OfferChat`, default `true`); set `OfferChat="false"` where the avatar sits in something that reacts to clicks. New library page `person` at `/people/{id}`.

### New

- Eight more built-in themes, each with light and dark palettes, own fonts, radius and options: Octo, Code, Arctic, Vampire, Solar, Atom, Paper and Aubergine (CLI/spec `theme`, template, Studio gallery). Their text meets WCAG AA contrast. See [Theming](../modules/theming.md#built-in-themes).
- The setup token is easy to find in a local run: the app host gives every service one token and, while the system is not set up, prints it on its console (`coworkee run`), in the api's log and in the Aspire dashboard (*Setup* link with the token on the web resource, *Setup token* property on the api). Coworkee Studio shows it with a copy button on the run card and under *My apps*; `coworkee deploy` names `<APP>_SETUP_TOKEN` when no administrator is set. `/setup?token=...` fills the token in. See [Setup wizard or seed](../security/authentication.md#setup-wizard-or-seed).
- `@coworkee/airworks`: *Inbox* (`coworkee.inbox`, approvals and notifications) replaces `notificationsApp`; *Records* with a record window (`coworkee.record`: fields, history with undo and restore, workflow); the profile is the first tab of *Settings* (`profileApp` is gone, `renderProfile` stays); the API documentation moved into the administration (*Monitoring*). Apps that registered `notificationsApp`, `profileApp` or `apiDocsApp` by name take `defaultApps` or the new names.
- Pages of the web app in AirWorks windows: `embedApp`, `openWebPage`; `CoworkeeLayout` shows only the page with `?embed=1`. `BffOptions.WebAppUrl` (reported as `appUrl` of `/bff/user`) and `BffOptions.FrameAncestors` (lets those origins frame the pages; antiforgery's `X-Frame-Options` is suppressed then); `AddAirWorks` sets both under Aspire.
- Badges on the inbox and chat tiles with AirWorks' `setBadge` (AirWorks 0.5; older versions just show none).
- Chat: group chats (name, add and remove people, leave), formatted messages with the rich text editor (Enter sends), `@` mentions of people (link to the person, notification for members of the chat) and `#` mentions of records from the global search (shown only to readers whose search finds them, otherwise *restricted*), replies, emoji reactions, editing and deleting own messages, files and pictures by button, drag and drop or paste with upload progress (members only, setting `Chat.MaxAttachmentMb`, hook `IChatAttachmentScanner`), read marks per reader, typing indicator and search in a chat. The server sanitizes every message. See [Chat](../modules/chat.md).
- Mini chats: a dock at the bottom right with up to three collapsible chat windows (unread count when minimized, *Open in the chat*, kept per user across reloads, full screen on phones). They open from the chat page, the command palette (**Chat with…**, `@name`), any avatar, the person page, the snackbar of a new message and mention notifications. `ChatDock` opens them from own components.
- `RichTextEditor` takes `Tools` (toolbar) and `Placeholder`.

### Fixes

- A new app run locally showed its setup page instead of signing in with the development users: the migrations project has no launch profile and ran as Production, so `DemoSeed` was skipped. `AddMigrations` passes the app host's environment (`DOTNET_ENVIRONMENT`) to the migrations of a local run.
- The setup page of a system that is already set up goes to the app; a setup tab left open leaves for the app when it gets the focus again or another tab finishes the setup.
- Built-in themes were listed twice when requests seeded them at the same moment (SQL Server and SQLite let the unique index repeat null tenants). Seeding runs once at a time per process and removes such copies (the one an organisation uses stays).

## 0.0.1.30 (2026-10-16)

### New

- The audit log undoes changes of entity sets registered with `AddEntityHistory`: per entry *Undo this change*, *Restore the state before this change*, *Restore record* (soft delete) and *Open record history*, through the history preview and restore. `EntityHistoryRestoreRequest` has a `Mode` (`After`, `Before`, `Undo`), the preview marks fields a later entry changed (`ChangedLater`), and `GET /api/v1/history` lists the readable sets. `IEntityHistoryApi` has `GetSetsAsync` (own implementations add it).

## 0.0.1.29 (2026-10-16)

### New

- Workflow state as a server-side filter: every OData set whose entity has a workflow gets the virtual properties `WorkflowState` and `WorkflowAwaitsMe` for `$filter` and `$orderby` (a tenant-filtered subquery, or the `MirrorTo` column) and a facet `WorkflowState` with the states of the definition and *Awaiting my approval*; both need `Workflows.View`. `CoworkeeWorkflowColumn` for `CoworkeeDataTable` filters, sorts and groups by it; `WorkflowState<T>.WithColor(...)` colors a state. Replace `TemplateColumn` + `CoworkeeWorkflowActions` with `<CoworkeeWorkflowColumn T="..." EntityType="..." />`.
- OData sets can carry computed properties of other modules (`IODataComputedPropertySource`, `ODataComputedProperty`): bound into `$filter`/`$orderby`, never serialized, optional facet. The library configures the OData route itself now instead of Nextended's `AddODataAuto`.

### Fixes

- `SyncDevelopmentUsersAsync()` runs for the migrations of a local Aspire run: the migrations project has no launch profile and ran as Production, so the sync never happened there. `AddMigrations` sets `Coworkee:Identity:SyncDevelopmentUsers` in local development runs; the sync runs in Development or with that setting.
- `SyncDevelopmentUsersAsync()` also gives seed users the roles and seeded roles the permissions added to the seed since (Development only, nothing is removed), so a database set up earlier picks up e.g. a new role of a demo user and new workflow permissions of a demo role.

## 0.0.1.28 (2026-10-16)

### Breaking changes

- **Migration needed** (apps with Reporting): new table `cw.DashboardReports`.
- **`IReportsApi`** gained `GetDashboardAsync` and `SetDashboardAsync`; own implementations add them. New endpoints under `/api/v1/reports/dashboard`: regenerate OpenAPI snapshots and SDKs.
- **`AddCommandPaletteEntitySet` and `CommandPaletteEntitySet<T>` are removed**: the command palette finds records through the new global search of the server. Register the set next to its OData set, e.g. `services.AddGlobalSearch<Product>(s => s.Fields(p => p.Name, p => p.Barcode).Page("/catalog/products"))` (`coworkee update` points at the old calls).
- New endpoints `/api/v1/search`, `/api/v1/search/stream` and `/api/v1/search/providers`: regenerate OpenAPI snapshots and SDKs.
- **`ICoworkeeApi`** gained `GetMailTemplateHistoryAsync`; own implementations add it.
- **MudBlazor.Extensions 9.11.4 and MudExRichTextEditor 9.5.5**: apps that pin MudBlazor.Extensions themselves move to 9.11.4.

### New

- A report as dashboard: every user can make a report their dashboard, holders of the new permission `Reports.Dashboard` the organisation's default; the user's choice wins. `ReportDashboard` shows it live and falls back to the app's own dashboard. The template's dashboard page uses it, lists *Dashboard* before *Reports* and the sample setup makes *Catalog overview* the organisation's dashboard. See [A report as dashboard](../modules/reporting.md#a-report-as-dashboard).
- **Global search**: one search over users, roles, groups, files, reports, chats and the app's own sets (`AddGlobalSearch<T>` for OData sets, `IGlobalSearchProvider` for anything else, `AddGlobalSearchIndex` for an Elasticsearch alias). Prefix before part, `"exact"`, `users:max` type filters and ids; scores comparable across providers, a timeout per provider, the same permissions, organisation and row rules as the OData lists. The command palette shows the hits group by group as they arrive, `/search?q=` lists all with a filter per type, `GET /api/v1/search/stream` streams JSON lines through the BFF, and the MCP tool `global_search` hands them to AI clients. See [Global search](../web/global-search.md).
- The files page opens the preview of `?file={id}`.
- Template samples: brands, products and documents are in the global search.
- `coworkee module add` and `coworkee entity add` register generated entities with text properties in the global search (`AddGlobalSearch<T>` with those fields and the list page).
- **Rich text editor setting**: `Editors.RichTextEditor` (TipTap, the default, or Quill) picks the engine of every rich text editor of the library; `RichTextEditor` brings the same choice to own pages. Mail placeholders work as mentions in both. See [Mailing](../modules/mailing.md#editing-a-template).
- **Mail template editor**: the editor fills the window, a preview sheet follows every change (desktop or phone width, light or dark mail client, inbox line), version history with a line diff and restore (`GET /api/v1/mail/templates/{name}/{culture}/history`), a guard against leaving unsaved changes, language chips next to the title and a searchable placeholder list.
- **Workflow designer**: `Coworkee.Workflows.Elsa.Studio` hosts Elsa Studio as an app of its own that signs in at the auth server (permission `Workflows.Elsa.Manage`) and works with the Elsa API of the api; `app.AddElsaStudio<T>()` (or `AddProjects()` for `*_ElsaStudio`) wires the client, the API address and the designer link, `coworkee deploy --without elsastudio` leaves it out. Template option `elsaStudio` (`--elsa-studio`). See [Elsa Studio](../modules/workflows-elsa.md#elsa-studio).

### Fixes

- Dialogs and side sheets of the library (import wizard, campaign wizard, theme, logo, code editor, API key, workflow comment) carry the initial class of MudBlazor.Extensions, so they show up only at their final position instead of flashing in the middle first. Own dialogs opened with `ShowExAsync` or `ShowSideSheetAsync` set `Class="@MudExCss.Classes.Dialog.Initial"` on their `MudDialog`.
- The approvals inbox shows title, details and deadline of an approval on lines of their own.
- Apps with `Coworkee.Workflows.Elsa` keep the property names of their validation errors (`Brand.Name`); the Elsa API (FastEndpoints) had switched FluentValidation to camelCase for every validator (`brand.Name`).
- Elsa's endpoints (`/api/elsa/...`) no longer land in the app's OpenAPI document: regenerate OpenAPI snapshots and SDKs of apps with `Coworkee.Workflows.Elsa`.


## 0.0.1.27 (2026-10-16)

### Fixes

- Links to addresses of the server (e.g. *Sign in* at the end of the setup, `/bff/login`) load from the server again; since the library pages have a catch-all route, the app showed *Not found.* for them. Nothing to change in apps.

## 0.0.1.26 (2026-10-16)

### Breaking changes

- **`ICoworkeeApi`** gained `GetSystemStatusAsync`; own implementations add it.
- **Library pages under their own prefix**: admin pages of the library moved from `/admin/...` to `/cw/admin/...` (`CoworkeeClientOptions.PagePrefix`, `""` keeps the old addresses); old addresses go on to the new ones. The library pages have no `@page` routes any more, one catch-all page (`/{*Path}`) renders them: an app with its own catch-all page gets an ambiguous route. Apps can replace or turn off pages by key (`options.Pages`), see [Replacing components](../ui/customizing.md#pages-of-the-library).
- **Deployments take the app host's tools along**: a published app host now deploys Keycloak, Grafana, WebDataStudio, LibreTranslate and AspireUI too. Apps from an older template copy three things from a new one: in `Migrations/Program.cs` the seed of `InitialAdmin.From(configuration)` (with `DemoSeed.ConfigureDeployment`) and `ReconcileKeycloakRealmAsync()`, in `ServiceDefaults/Extensions.cs` the Grafana exporter per signal with `GRAFANA_OTLP_PROTOCOL`, and in `AppHost.cs` `app.Deploys(...)` for Ollama and `aspireUi.WithCoworkee(app)` for AspireUI. `KeycloakRealm` moved to `Coworkee.Contracts.Identity`.
- **Migration needed**: new tables `cw.PushSubscriptions` (Notifications), `cw.DataTransferJobs` and `cw.ImportProfiles` (OData, so every app). Workflows and Webhooks bring their own tables when an app adds them.
- **`ICoworkeeApi`** gained `AddMailTemplateLanguageAsync`, `GetPushSettingsAsync`, `SavePushSubscriptionAsync` and `DeletePushSubscriptionAsync`; own implementations add them.
- **`Notification`** derives from `AggregateRoot` now (it raises `NotificationCreated` while push is configured). No schema change from that.
- **`IODataClient.ExportAsync` and `ImportAsync` removed**: use `IDataTransferApi` (export of the current view in three formats, the import wizard). Own `IODataClient` fakes drop the two methods.
- **`ODataImportRegistration`** has `Create`, `Update` and `Delete` instead of `Send`; `AddODataImport` takes `(Guid? id, row) => command` and an optional delete command for updates and undo.
- **`CoworkeeODataModule` depends on `CoworkeeBackgroundJobsModule`**: imports and large exports run as jobs. Apps from the template have it already (mailing); test hosts set `Coworkee:Jobs:ConnectionStringName` and create their tables before `UseCoworkee()`.
- The *Import* button of `CoworkeeDataTable` opens the import wizard instead of uploading an Excel file at once; Excel, CSV and JSON exports of OData sets come from the server with the visible columns.
- Phone layout: apps add `viewport-fit=cover` to the viewport tag of `App.razor` (`coworkee update` does it).
- New endpoints (push, mail template languages, import and export, record history, workflows, webhooks): regenerate OpenAPI snapshots and SDKs.

`coworkee update` knows all of these (upgrade rules of 0.0.1.26).

### New

- Template samples: the command palette offers *New product* (opens the dialog through `/catalog/products?new=true`) and finds products by name or barcode.
- **Webhooks: rotate secret**: a new secret shown once, the old one signs next to it for `Coworkee:Webhooks:SecretGracePeriod` (24 h), see [Webhooks](../modules/webhooks.md). Two new columns on `cw.WebhookSubscriptions` (apps with webhooks need a migration).
- **Workflow chips in tables** load the states of a page in one request (`POST /api/v1/workflows/{entityType}/states`, `IWorkflowApi.GetStatesAsync`) instead of one per row; own `IWorkflowApi` implementations add the method.
- **Range sliders in column filters**: *between* on number and date columns slides between the smallest and largest value the server reports; a column with a date or number range facet (e.g. the demo's order date) shows that facet's histogram and slider instead of a check box per bucket.
- **System status** page for the system organisation (`/cw/admin/system-status`): versions, database and pending migrations, outbox, background jobs with the last failures, mail, realtime, Redis, files, translation and AI; modules and apps add cards with `ISystemStatusContributor`, see [Admin UI](../features/blazor-ui.md#system-status).
- **Workflows and approvals** (`Coworkee.Workflows`, optional): states and guarded transitions per entity type, approvals by roles or users with deadline, reminder and escalation, a history, the components `CoworkeeWorkflow`, `CoworkeeWorkflowActions` and `CoworkeeWorkflowTimeline` and the page *Approvals*, see [Workflows and approvals](../modules/workflows.md).
- **Elsa workflows** (`Coworkee.Workflows.Elsa`, optional): Elsa Workflows 3 in the API with Coworkee activities and triggers, see [Elsa workflows](../modules/workflows-elsa.md).
- **Webhooks** (`Coworkee.Webhooks`, optional): endpoints per organisation get signed events (Standard Webhooks style) about changes of opted-in entity sets, workflow transitions and the app's own events, shaped by what the endpoint's owner may read; retries with backoff, switch-off after repeated failures, delivery log, test events and redelivery on `/admin/webhooks`, SSRF protection. See [Webhooks](../modules/webhooks.md).
- **Import wizard** per entity set: CSV, Excel and JSON, detected encoding, separator and header, column mapping with suggestions and saved mappings, conversion with the file's language, preview with the validation of the commands, add, update by key or both, dry run, background job with live progress, file of the failed rows and undo of the last import, see [Import and export](../web/import-export.md). **Admin page** *Import and export* (`/admin/data-transfer`, permission `DataTransfer.Use`; grant it to the roles that should see it). **Exports** of the current view as Excel, CSV or JSON (`format`, `columns`); more than `Coworkee:OData:BackgroundExportRows` rows (10 000) run as a job with a download notice.
- **Record history and restore** (`AddEntityHistory`, `History="true"` on `CoworkeeDataTable`, `CoworkeeEntityHistory`): timeline with word diff, restore of a state or a single field with preview, deleted records of `ISoftDelete` sets come back, see [Auditing](../modules/auditing.md#record-history-and-restore).
- **Command palette** (Ctrl+K): pages, records, actions and the assistant as fallback, extensible with `AddCommandPaletteProvider`, see [Command palette](../ui/command-palette.md).
- **Phone layout** of the Blazor shell and the sign-in pages: overflow menu in the app bar, navigation as overlay, full-screen edit sheets, facet menus as bottom sheets, sticky row actions, 44 px touch targets and safe areas, see [Phones, installable app and push](../ui/phones-and-pwa.md).
- **Installable app**: `<Coworkee.Bff.Pwa.CoworkeePwa />` in the head of `App.razor` links the web manifest in the theme's colours; `Coworkee.Bff` serves manifest, service worker (it keeps only the offline page), icon and offline page; update and install prompts in the app.
- **Web Push** for the notifications: `options.UsePush()` in Aspire, or `Coworkee:Notifications:Push:PublicKey`/`PrivateKey`; users switch it on per device on the notifications page.
- **Languages of mail templates**: *Add language* on Administration > Mail templates adds a variant for a language switched on later, copied or machine translated (texts only, tags and placeholders kept), and removes it again, see [Mailing](../modules/mailing.md#languages-of-a-template). A mail in a language without a variant uses the default language (organisation, system, Languages page) before English (`IDefaultLanguage`); machine translation is available to other modules as `IMachineTranslator`.
- **Deployment**: the app host publishes for Docker Compose, Azure Container Apps or Kubernetes (public addresses, token certificates, setup token, the first administrator and the mail server as parameters, stable volumes, fixed ports); the tools of the app host (Keycloak with its realm, Grafana with the app's dashboards, WebDataStudio, LibreTranslate, Ollama, AspireUI) go along with volumes and public addresses, each one can be left out (`DeployTool`, `coworkee deploy --without`), Mailpit only with `--with mailpit`; `coworkee deploy compose|azure|k8s` asks for the administrator and the mail server and lists every address and the sign-in at the end; Coworkee Studio offers *Deploy* after creating an app; token certificates can come as Base64 from a secret. Only services that process jobs create the Hangfire schema. See [Deployment](../hosting/deployment.md).
- **CLI**: `coworkee add`/`remove` turn template options on or off in an existing app (merged into your files, conflicts as `.rej`, options kept in `.coworkee/app.json`), `coworkee update` applies the upgrade rules of the versions in between, `coworkee db reset|seed|backup|restore`, `coworkee entity import --from db|openapi|csv`, six starter specs (`coworkee specs list|show`, `coworkee new --template-spec`), options `--workflows`, `--elsa`, `--webhooks` and `--pwa` (on by default). MCP tools `add_option` and `import_entities`. See [CLI](../getting-started/cli.md) and [Starter specs](../getting-started/gallery.md).
- **Studio**: open an existing app and change it, *My apps* (start, stop, links), *Start from a template*, entity diagram (drag, draw references, auto layout, PNG export) and entity import with a preview.
- **Template**: sample products are soft deleted and, with auditing, show their history and come back; the dashboard works on SQLite (it returned 500 there); demo users are seeded only in Development, migrations wait for the database.
- **`coworkee doctor`** compares versions inside an app: dotnet-ef with EF Core, Aspire CLI with the app host's Aspire SDK, coworkee with `CoworkeeVersion`, and finds ports of the launch profiles in use, each with a fix.
- **Development users stay in step**: `SyncDevelopmentUsersAsync()` (Development only) adds seed users and roles a database set up earlier lacks and gives seed users their seeded password again; the template's migrations host calls it after `SeedCoworkeeIdentityAsync()`, so changes to `DemoUsers.cs` reach an existing database.

## 0.0.1.25 (2026-10-15)

Fixes only: the loading screen script is its own file, so a strict CSP runs it, and app pages are sent with `Cache-Control: no-cache, no-store`. Nothing to change in apps.

## 0.0.1.24 (2026-10-15)

### Breaking changes

- **`ClientEntityIndicator` removed**: every data table of a mirrored set shows the `ClientEntitySwitch` in its tool bar (added by `AddCoworkeeClientEntities`). Delete the `<ClientEntityIndicator>` lines and an own *Compare* button from your pages.
- **`ODataPicker.Margin`** (and `OptionalODataPicker.Margin`) is `Margin?` now: unset, the picker follows the density of the theme. Code that reads the value as `Margin` adds `?? Margin.Dense`.
- **`CliOption`** (Coworkee.Cli.Spec) gained the constructor parameter `DependsOn`, `OptionDependency` gained `Or`: source compatible, binary incompatible for tools compiled against an older Coworkee.Cli.Spec.
- **`LoadingScreenDto`** gained the positional parameter `SignedIn`; regenerate OpenAPI snapshots and SDKs.
- **Enums in every `MudExObjectEdit`** render with the `EnumEditor` (localized labels from `EnumLabels`, `[Flags]` as multi select of the single-bit members) and nullable enums with the clearable `NullableEnumEditor`. A meta's `RenderWith` still wins.
- **`--sign-in-media` with the layout `Card`** is ignored (not passed, not copied) unless `--loading-screen SignInMedia` shows it behind the loading screen.

### New

- **Loading screen** in the theme's colours with the real download progress, a friendly error state when the start fails, and the user's colour mode, theme and language, see [Theming](../modules/theming.md#loading-screen). **Every app adds one line** as the first line of `<body>` in `MyApp.Web/App.razor`: `<Coworkee.Bff.CoworkeeLoadingScreen Logo="coworkee-icon.svg" />`. Its background (`Animated`, `SignInMedia`, `Plain`) is set on the *Sign-in page* card, with `options.SignIn.LoadingScreen` in Aspire or `--loading-screen` in the CLI. The page is now sent with `Cache-Control: no-cache, no-store`; the logo comes from `/bff/loading-screen/logo`, the script from `/bff/loading-screen/script.js` (no inline script, so apps with a strict Content-Security-Policy keep working).
- **Public files**: managers make a file public (`PUT /api/v1/files/{id}/public`, file manager bar, preview); anyone loads it from `GET /api/v1/files/public/{id}/{name}`, e.g. as sign-in or loading screen media, picked with *Choose from files*. **The model gained `StoredFile.IsPublic`: apps with the Files module add a migration.**
- **Page mode of data tables**: `CoworkeeDataTable.PageMode` (`Pager`, `EndlessScroll`) and the setting `DataTable.DefaultPageMode` (global and per user), see [Data table](../ui/data-table.md).
- **Client entity switch** in the tool bar of mirrored tables: search in the browser or on the server, sync state as a ring, *Compare and choose the faster* picks the faster side; the choice is kept per user.
- `ImageDataUrlEdit` uploads with `MudExUploadEdit`; enums get the `EnumEditor` everywhere (see Breaking changes).
- The texts of the MudBlazor.Extensions components the library shows (upload field, style and cron editors, file manager, file preview) ship in all languages; their stored machine translations are replaced at the next start.
- CLI and Studio: options that need another one (`--airworks-packages` without `--airworks`, `--ai-provider`, `--ai-model` and `--ai-endpoint` without `--ai`, `--backup` off Postgres, `--sign-in-media` with the layout `Card` unless the loading screen shows it, `--sign-in-media-side` without a media) are no longer passed to the template; the CLI warns (`coworkee spec validate` too) and the Studio hides their cards. Sign-in media of a `Card` layout is therefore not copied into the app any more. New `coworkee spec show` and a spec from the Studio that lists every library module with `true`/`false`; `coworkee options` prints `Needs:` and the websites of the products an option brings, the Studio cards show icons and open buttons, see [CLI](../getting-started/cli.md#options).

## 0.0.1.23 and earlier

### Breaking changes

- **`IMailSender`**: the interface now requires the six-parameter `QueueAsync(to, template, model, culture, campaignId, cancellationToken)`. The five-parameter overload is a default method that passes `null` as the campaign. Own implementations (test doubles, wrappers) implement the six-parameter method; callers are unchanged.
- **`IMailTemplateRenderer`**: gained `RenderContentAsync(content, layout, model, cancellationToken)`, which renders with an already resolved layout (the campaign job uses it to resolve template and layout once per run). Own implementations add the method; callers are unchanged.
- **Positional records gained parameters**: `MailTemplateSummaryDto` (`IsCustom`, `Description`), `MailTemplateDto` (`IsCustom`) and `OutgoingMailDto` (`CampaignId`). Code that constructs or deconstructs them positionally, and generated clients, need the new parameters. Regenerate OpenAPI snapshots and SDKs.
- **Removed with the MudEx 9.11.3 adoption**: `ChipListEditor`, `CronEditor`, `CronSchedule` and `SettingsFormRules.WrapInColumns`. Lists of strings render as `MudExTagField<string>` and `[Cron]` renders `MudExCronEdit`; use those directly.

### New

- AirWorks client: chat, reports with their designer, assistant, the administration apps (`adminApps`) and realtime, see [AirWorks client](../web/airworks.md). Generated apps take `@coworkee/client` and `@coworkee/airworks` from the tarballs inside the template; every release also publishes them to npm (`--airworks-packages npm`). `coworkee new --template-source` adds the local package folder to the app's `nuget.config`. Apps made with the first AirWorks version: add `...adminApps` to `ClientApp/src/main.ts` and, for the file preview, the MudEx hosting of the template (`mudex-webcomponents`, `vite.config.ts`, `.dat` content type in `Program.cs`).
- Mail campaigns with recipient presets, saved queries and manual selection, scheduled sending and an unsubscribe confirmation page, see [Mailing](../modules/mailing.md).
- Own mail templates with a visual and a source editor, and a Monaco based `CodeEditor` component.
- Entity model changes for campaigns, recipient lists and own templates: add a migration to your app.
