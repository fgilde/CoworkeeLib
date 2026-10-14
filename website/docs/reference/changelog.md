# Changelog

Changes that need action in apps built on Coworkee. Newest first.

## Unreleased

### Breaking changes

- **`IMailSender`**: the interface now requires the six-parameter `QueueAsync(to, template, model, culture, campaignId, cancellationToken)`. The five-parameter overload is a default method that passes `null` as the campaign. Own implementations (test doubles, wrappers) implement the six-parameter method; callers are unchanged.
- **`IMailTemplateRenderer`**: gained `RenderContentAsync(content, layout, model, cancellationToken)`, which renders with an already resolved layout (the campaign job uses it to resolve template and layout once per run). Own implementations add the method; callers are unchanged.
- **Positional records gained parameters**: `MailTemplateSummaryDto` (`IsCustom`, `Description`), `MailTemplateDto` (`IsCustom`) and `OutgoingMailDto` (`CampaignId`). Code that constructs or deconstructs them positionally, and generated clients, need the new parameters. Regenerate OpenAPI snapshots and SDKs.
- **Removed with the MudEx 9.11.3 adoption**: `ChipListEditor`, `CronEditor`, `CronSchedule` and `SettingsFormRules.WrapInColumns`. Lists of strings render as `MudExTagField<string>` and `[Cron]` renders `MudExCronEdit`; use those directly.

### New

- AirWorks client: chat, reports with their designer, assistant, the administration apps (`adminApps`) and realtime, see [AirWorks client](../web/airworks.md). Generated apps take `@coworkee/client` and `@coworkee/airworks` from the tarballs inside the template; every release also publishes them to npm (`--airworks-packages npm`). `coworkee new --template-source` adds the local package folder to the app's `nuget.config`. Apps made with the first AirWorks version: add `...adminApps` to `ClientApp/src/main.ts` and, for the file preview, the MudEx hosting of the template (`mudex-webcomponents`, `vite.config.ts`, `.dat` content type in `Program.cs`).
- CLI and Studio: options that need another one (`--airworks-packages` without `--airworks`, `--ai-provider`, `--ai-model` and `--ai-endpoint` without `--ai`, `--backup` off Postgres, `--sign-in-media` with the layout `Card`, `--sign-in-media-side` without a media) are no longer passed to the template; the CLI warns (`coworkee spec validate` too) and the Studio hides their cards. Sign-in media of a `Card` layout is therefore not copied into the app any more. New `coworkee spec show` and a spec from the Studio that lists every library module with `true`/`false`; `coworkee options` prints `Needs:` and the websites of the products an option brings, the Studio cards show icons and open buttons, see [CLI](../getting-started/cli.md#options).
- Mail campaigns with recipient presets, saved queries and manual selection, scheduled sending and an unsubscribe confirmation page, see [Mailing](../modules/mailing.md).
- Own mail templates with a visual and a source editor, and a Monaco based `CodeEditor` component.
- Entity model changes for campaigns, recipient lists and own templates: add a migration to your app.
