# Changelog

Changes that need action in apps built on Coworkee. Newest first.

## Unreleased

### Breaking changes

- **`IMailSender`**: the interface now requires the six-parameter `QueueAsync(to, template, model, culture, campaignId, cancellationToken)`. The five-parameter overload is a default method that passes `null` as the campaign. Own implementations (test doubles, wrappers) implement the six-parameter method; callers are unchanged.
- **Positional records gained parameters**: `MailTemplateSummaryDto` (`IsCustom`, `Description`), `MailTemplateDto` (`IsCustom`) and `OutgoingMailDto` (`CampaignId`). Code that constructs or deconstructs them positionally, and generated clients, need the new parameters. Regenerate OpenAPI snapshots and SDKs.
- **Removed with the MudEx 9.11.3 adoption**: `ChipListEditor`, `CronEditor`, `CronSchedule` and `SettingsFormRules.WrapInColumns`. Lists of strings render as `MudExTagField<string>` and `[Cron]` renders `MudExCronEdit`; use those directly.

### New

- AirWorks client: chat, reports, assistant, the administration apps (`adminApps`) and realtime, see [AirWorks client](../web/airworks.md). `@coworkee/client` and `@coworkee/airworks` are published to npm with every release; generated apps reference the npm version, local template builds the tarballs (`npmPackages=vendor`). Apps made with the first AirWorks version: add `...adminApps` to `ClientApp/src/main.ts` and, for the file preview, the MudEx hosting of the template (`mudex-webcomponents`, `vite.config.ts`, `.dat` content type in `Program.cs`).
- Mail campaigns with recipient presets, saved queries and manual selection, scheduled sending and an unsubscribe confirmation page, see [Mailing](../modules/mailing.md).
- Own mail templates with a visual and a source editor, and a Monaco based `CodeEditor` component.
- Entity model changes for campaigns, recipient lists and own templates: add a migration to your app.
