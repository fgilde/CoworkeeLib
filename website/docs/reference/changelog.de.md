# Änderungsprotokoll

Änderungen, die in Apps auf Basis von Coworkee Handlungsbedarf auslösen. Neueste zuerst.

## Unveröffentlicht

### Breaking Changes

- **`IMailSender`**: Die Schnittstelle verlangt jetzt `QueueAsync(to, template, model, culture, campaignId, cancellationToken)` mit sechs Parametern. Die Überladung mit fünf Parametern ist eine Default-Methode, die `null` als Kampagne übergibt. Eigene Implementierungen (Testdoubles, Wrapper) implementieren die Methode mit sechs Parametern; Aufrufer bleiben unverändert.
- **`IMailTemplateRenderer`**: hat `RenderContentAsync(content, layout, model, cancellationToken)` dazubekommen, das mit einem bereits aufgelösten Layout rendert (der Kampagnen-Job löst damit Vorlage und Layout einmal pro Lauf auf). Eigene Implementierungen ergänzen die Methode; Aufrufer bleiben unverändert.
- **Positionale Records haben Parameter dazubekommen**: `MailTemplateSummaryDto` (`IsCustom`, `Description`), `MailTemplateDto` (`IsCustom`) und `OutgoingMailDto` (`CampaignId`). Code, der sie positional erzeugt oder dekonstruiert, und generierte Clients brauchen die neuen Parameter. OpenAPI-Snapshots und SDKs neu erzeugen.
- **Entfernt mit der Übernahme von MudEx 9.11.3**: `ChipListEditor`, `CronEditor`, `CronSchedule` und `SettingsFormRules.WrapInColumns`. String-Listen werden als `MudExTagField<string>` dargestellt, `[Cron]` als `MudExCronEdit`; verwenden Sie diese direkt.

### Neu

- AirWorks-Client: Chat, Berichte mit Designer, Assistent, die Administrations-Apps (`adminApps`) und Realtime, siehe [AirWorks-Client](../web/airworks.md). Erzeugte Apps nehmen `@coworkee/client` und `@coworkee/airworks` aus den Tarballs im Template; jedes Release veröffentlicht sie zusätzlich auf npm (`--airworks-packages npm`). `coworkee new --template-source` trägt den lokalen Paketordner in die `nuget.config` der App ein. Apps aus der ersten AirWorks-Version: `...adminApps` in `ClientApp/src/main.ts` ergänzen und für die Dateivorschau das MudEx-Hosting des Templates übernehmen (`mudex-webcomponents`, `vite.config.ts`, `.dat`-Content-Type in `Program.cs`).
- Mail-Kampagnen mit Empfänger-Voreinstellungen, gespeicherten Abfragen und manueller Auswahl, geplantem Versand und einer Bestätigungsseite für die Abmeldung, siehe [Mailing](../modules/mailing.md).
- Eigene Mail-Vorlagen mit visuellem und Quelltext-Editor sowie eine `CodeEditor`-Komponente auf Basis von Monaco.
- Änderungen am Entitätsmodell für Kampagnen, Empfängerlisten und eigene Vorlagen: Fügen Sie Ihrer App eine Migration hinzu.
