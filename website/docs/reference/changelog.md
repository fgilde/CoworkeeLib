# Changelog

Changes that need action in apps built on Coworkee. Newest first.

## Unreleased

### Breaking changes

- **`ICoworkeeApi`** gained `GetPushSettingsAsync`, `SavePushSubscriptionAsync` and `DeletePushSubscriptionAsync`; own implementations add them.
- **`Notification`** derives from `AggregateRoot` now (it raises `NotificationCreated` while push is configured). No schema change from that.
- **The model gained `PushSubscription`** (table `cw.PushSubscriptions`): apps with the Notifications module add a migration.
- **New endpoints** `GET`/`PUT /api/v1/notifications/push` and `DELETE /api/v1/notifications/push/{id}`; regenerate OpenAPI snapshots and SDKs.

### New

- **Phone layout** of the Blazor shell and the sign-in pages: overflow menu in the app bar, navigation as overlay that closes after a click, full-screen edit sheets, facet menus as bottom sheets, page headers that wrap, sticky row actions, 44 px touch targets and safe areas, see [Phones, installable app and push](../ui/phones-and-pwa.md). Apps add `viewport-fit=cover` to the viewport tag of `App.razor`.
- **Installable app**: `<Coworkee.Bff.Pwa.CoworkeePwa />` in the head of `App.razor` links the web manifest in the theme's colours; `Coworkee.Bff` serves manifest, service worker (it keeps only the offline page), icon and offline page. Update and install prompts in the app. Template option `pwa` (on by default).
- **Web Push** for the notifications: `options.UsePush()` in Aspire, or `Coworkee:Notifications:Push:PublicKey`/`PrivateKey`; users switch it on per device on the notifications page.

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
