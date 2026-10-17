# Änderungsprotokoll

Änderungen, die in Apps auf Basis von Coworkee Handlungsbedarf auslösen. Neueste zuerst.

## Unveröffentlicht

### Breaking Changes

- **Migration nötig** (Apps mit Chat): Direkt- und Gruppenchats bringen die Tabellen `cw.ChatConversations`, `cw.ChatMembers`, `cw.ChatReactions`, `cw.ChatAttachments` und neue Spalten von `cw.ChatMessages`. Frühere Direktnachrichten wandern beim nächsten Start in Unterhaltungen; Gelesenes bleibt gelesen.
- **`Coworkee.Chat` hängt von `Coworkee.Storage` ab** (Anhänge) und referenziert `Coworkee.Notifications` (Benachrichtigungen bei Erwähnungen, wenn das Modul da ist).
- **Chat-Verträge**: `ChatMessageDto.ToUserId` ist nullable (leer in Gruppen), das DTO hat neue Eigenschaften (`ConversationId`, `Html`, `Source`, `EditedAt`, `Deleted`, `ReplyTo`, `Reactions`, `Attachments`, `HasEntityMentions`); `ChatReadDto` hat `ConversationId`. `IChatApi` hat die Mitglieder für Unterhaltungen dazubekommen; eigene Implementierungen ergänzen sie. Neue Endpunkte unter `/api/v1/chat/chats`, `/messages` und `/attachments`: OpenAPI-Snapshots und SDKs neu erzeugen. Die Klartext-Endpunkte pro Person bleiben.
- **`UserAvatar` öffnet ein Menü mit *Chat* und *Profil*** für andere Personen, wenn der Benutzer chatten darf (`OfferChat`, Standard `true`); setze `OfferChat="false"`, wo der Avatar in etwas sitzt, das auf Klicks reagiert. Neue Bibliotheksseite `person` unter `/people/{id}`.

### Neu

- Acht weitere eingebaute Themes, jeweils mit heller und dunkler Palette, eigener Schrift, Radius und Optionen: Octo, Code, Arctic, Vampire, Solar, Atom, Paper und Aubergine (CLI/Spec `theme`, Template, Studio-Galerie). Ihr Text erfüllt den Kontrast nach WCAG AA. Siehe [Theming](../modules/theming.md#eingebaute-themes).
- Das Setup-Token ist bei einem lokalen Lauf leicht zu finden: Der AppHost gibt allen Diensten dasselbe Token und zeigt es, solange das System nicht eingerichtet ist, in seiner Konsole (`coworkee run`), im Log der API und im Aspire-Dashboard (Link *Setup* mit Token an der Web-Ressource, Eigenschaft *Setup token* an der API). Coworkee Studio zeigt es mit Kopier-Knopf in der Lauf-Karte und unter *Meine Apps*; `coworkee deploy` nennt `<APP>_SETUP_TOKEN`, wenn kein Administrator gesetzt ist. `/setup?token=...` füllt das Token ein. Siehe [Setup-Assistent oder Seed](../security/authentication.md#setup-assistent-oder-seed).
- `@coworkee/airworks`: *Eingang* (`coworkee.inbox`, Genehmigungen und Benachrichtigungen) ersetzt `notificationsApp`; *Datensätze* mit einem Datensatzfenster (`coworkee.record`: Felder, Verlauf mit Rückgängig und Wiederherstellen, Workflow); das Profil ist der erste Tab der *Einstellungen* (`profileApp` entfällt, `renderProfile` bleibt); die API-Dokumentation liegt in der Administration (*Überwachung*). Apps, die `notificationsApp`, `profileApp` oder `apiDocsApp` einzeln registriert haben, nehmen `defaultApps` oder die neuen Namen.
- Seiten der Web-App in AirWorks-Fenstern: `embedApp`, `openWebPage`; `CoworkeeLayout` zeigt mit `?embed=1` nur die Seite. `BffOptions.WebAppUrl` (als `appUrl` von `/bff/user`) und `BffOptions.FrameAncestors` (diese Origins dürfen die Seiten einrahmen; das `X-Frame-Options` des Antiforgery entfällt dann); `AddAirWorks` setzt beides unter Aspire.
- Badges auf den Kacheln von Eingang und Chat mit `setBadge` von AirWorks (AirWorks 0.5; ältere Versionen zeigen keine).
- Chat: Gruppenchats (Name, Personen hinzufügen und entfernen, austreten), formatierte Nachrichten mit dem Rich-Text-Editor (Enter sendet), `@`-Erwähnungen von Personen (Link auf die Person, Benachrichtigung für Mitglieder des Chats) und `#`-Erwähnungen von Datensätzen aus der globalen Suche (nur für Leser sichtbar, deren Suche sie findet, sonst *eingeschränkt*), Antworten, Emoji-Reaktionen, eigene Nachrichten bearbeiten und löschen, Dateien und Bilder per Button, Drag-and-drop oder Einfügen mit Upload-Fortschritt (nur für Mitglieder, Einstellung `Chat.MaxAttachmentMb`, Erweiterung `IChatAttachmentScanner`), Lesebestätigungen pro Leser, Tipp-Anzeige und Suche im Chat. Der Server bereinigt jede Nachricht. Siehe [Chat](../modules/chat.md).
- Mini-Chats: ein Dock unten rechts mit bis zu drei einklappbaren Chatfenstern (ungelesene Nachrichten im minimierten Zustand, *Im Chat öffnen*, pro Benutzer über ein Neuladen hinweg gemerkt, auf dem Handy bildschirmfüllend). Sie öffnen sich von der Chatseite, der Befehlspalette (**Chatten mit…**, `@name`), jedem Avatar, der Personenseite, der Snackbar einer neuen Nachricht und Erwähnungs-Benachrichtigungen. `ChatDock` öffnet sie aus eigenen Komponenten.
- `RichTextEditor` nimmt `Tools` (Werkzeugleiste) und `Placeholder`.

### Korrekturen

- Eine neue App zeigte lokal ihre Setup-Seite, statt mit den Entwicklungsbenutzern anzumelden: Das Migrationsprojekt hat kein Launch-Profil und lief als Production, also fiel `DemoSeed` weg. `AddMigrations` gibt den Migrationen eines lokalen Laufs die Umgebung des AppHosts mit (`DOTNET_ENVIRONMENT`).
- Die Setup-Seite eines bereits eingerichteten Systems führt zur App; ein offener Setup-Tab wechselt zur App, sobald er wieder den Fokus bekommt oder ein anderer Tab das Setup abschließt.
- Eingebaute Themes standen doppelt in der Liste, wenn Anfragen sie gleichzeitig anlegten (SQL Server und SQLite lassen im eindeutigen Index leere Mandanten mehrfach zu). Das Anlegen läuft pro Prozess nur einmal gleichzeitig und entfernt solche Kopien (die Kopie, die eine Organisation nutzt, bleibt).

## 0.0.1.30 (2026-10-16)

### Neu

- Das Audit-Log macht Änderungen von Entity-Sets mit `AddEntityHistory` rückgängig: pro Eintrag *Diese Änderung rückgängig machen*, *Stand vor dieser Änderung wiederherstellen*, *Datensatz wiederherstellen* (Soft Delete) und *Verlauf des Datensatzes öffnen*, über die Vorschau und das Wiederherstellen des Verlaufs. `EntityHistoryRestoreRequest` hat einen `Mode` (`After`, `Before`, `Undo`), die Vorschau markiert Felder, die ein späterer Eintrag geändert hat (`ChangedLater`), und `GET /api/v1/history` listet die lesbaren Sets. `IEntityHistoryApi` hat `GetSetsAsync` (eigene Implementierungen ergänzen es).

## 0.0.1.29 (2026-10-16)

### Neu

- Workflow-Zustand als Filter auf dem Server: Jede OData-Menge, deren Entität einen Workflow hat, bekommt die virtuellen Eigenschaften `WorkflowState` und `WorkflowAwaitsMe` für `$filter` und `$orderby` (eine nach Mandant gefilterte Unterabfrage oder die `MirrorTo`-Spalte) und eine Facette `WorkflowState` mit den Zuständen der Definition und *Wartet auf meine Genehmigung*; beide brauchen `Workflows.View`. `CoworkeeWorkflowColumn` für `CoworkeeDataTable` filtert, sortiert und gruppiert danach; `WorkflowState<T>.WithColor(...)` färbt einen Zustand. Ersetze `TemplateColumn` + `CoworkeeWorkflowActions` durch `<CoworkeeWorkflowColumn T="..." EntityType="..." />`.
- OData-Mengen können berechnete Eigenschaften anderer Module tragen (`IODataComputedPropertySource`, `ODataComputedProperty`): in `$filter`/`$orderby` gebunden, nie serialisiert, optional mit Facette. Die OData-Route konfiguriert jetzt die Bibliothek selbst statt Nextendeds `AddODataAuto`.

### Korrekturen

- `SyncDevelopmentUsersAsync()` läuft für die Migrationen eines lokalen Aspire-Laufs: Das Migrationsprojekt hat kein Startprofil und lief als Production, dort fand der Abgleich nie statt. `AddMigrations` setzt in lokalen Entwicklungsläufen `Coworkee:Identity:SyncDevelopmentUsers`; der Abgleich läuft in Development oder mit dieser Einstellung.
- `SyncDevelopmentUsersAsync()` gibt Seed-Benutzern auch die seitdem im Seed ergänzten Rollen und Seed-Rollen die ergänzten Berechtigungen (nur in Development, entfernt wird nichts); so bekommt eine früher eingerichtete Datenbank z. B. eine neue Rolle eines Demo-Benutzers und neue Workflow-Berechtigungen einer Demo-Rolle.

## 0.0.1.28 (2026-10-16)

### Breaking Changes

- **Migration nötig** (Apps mit Reporting): neue Tabelle `cw.DashboardReports`.
- **`IReportsApi`** hat `GetDashboardAsync` und `SetDashboardAsync` bekommen; eigene Implementierungen ergänzen sie. Neue Endpunkte unter `/api/v1/reports/dashboard`: OpenAPI-Snapshots und SDKs neu erzeugen.
- **`AddCommandPaletteEntitySet` und `CommandPaletteEntitySet<T>` sind entfernt**: Die Befehlspalette findet Datensätze über die neue globale Suche des Servers. Registriere das Set neben seinem OData-Set, z. B. `services.AddGlobalSearch<Product>(s => s.Fields(p => p.Name, p => p.Barcode).Page("/catalog/products"))` (`coworkee update` zeigt auf die alten Aufrufe).
- Neue Endpunkte `/api/v1/search`, `/api/v1/search/stream` und `/api/v1/search/providers`: OpenAPI-Snapshots und SDKs neu erzeugen.
- **`ICoworkeeApi`** hat `GetMailTemplateHistoryAsync` bekommen; eigene Implementierungen ergänzen es.
- **MudBlazor.Extensions 9.11.4 und MudExRichTextEditor 9.5.5**: Apps, die MudBlazor.Extensions selbst festlegen, gehen auf 9.11.4.

### Neu

- Ein Bericht als Dashboard: jeder Benutzer kann einen Bericht zu seinem Dashboard machen, Inhaber der neuen Berechtigung `Reports.Dashboard` zum Standard der Organisation; die Wahl des Benutzers gewinnt. `ReportDashboard` zeigt es live an und fällt auf das eigene Dashboard der App zurück. Die Dashboard-Seite des Templates nutzt es, führt *Dashboard* vor *Berichte* auf, und die Einrichtung mit Beispielen macht *Catalog overview* zum Dashboard der Organisation. Siehe [Ein Bericht als Dashboard](../modules/reporting.md#ein-bericht-als-dashboard).
- **Globale Suche**: eine Suche über Benutzer, Rollen, Gruppen, Dateien, Berichte, Chats und die eigenen Sets der App (`AddGlobalSearch<T>` für OData-Sets, `IGlobalSearchProvider` für alles andere, `AddGlobalSearchIndex` für einen Elasticsearch-Alias). Anfang vor Teil, `"exakt"`, Typfilter wie `users:max` und Ids; Bewertungen über Provider hinweg vergleichbar, ein Timeout pro Provider, dieselben Berechtigungen, Organisation und Zeilenregeln wie die OData-Listen. Die Befehlspalette zeigt die Treffer Gruppe für Gruppe, sobald sie da sind, `/search?q=` listet alle mit einem Filter pro Typ, `GET /api/v1/search/stream` streamt JSON-Zeilen durch das BFF, und das MCP-Tool `global_search` gibt sie an KI-Clients. Siehe [Globale Suche](../web/global-search.md).
- Die Dateiseite öffnet die Vorschau von `?file={id}`.
- Beispiele der Vorlage: Marken, Produkte und Dokumente sind in der globalen Suche.
- `coworkee module add` und `coworkee entity add` tragen erzeugte Entitäten mit Texteigenschaften in die globale Suche ein (`AddGlobalSearch<T>` mit diesen Feldern und der Listenseite).
- **Einstellung Rich-Text-Editor**: `Editors.RichTextEditor` (TipTap, der Standard, oder Quill) wählt den Editor für jeden Rich-Text-Editor der Bibliothek; `RichTextEditor` bringt dieselbe Wahl auf eigene Seiten. Mail-Platzhalter funktionieren in beiden als Erwähnungen. Siehe [Mailing](../modules/mailing.md#vorlage-bearbeiten).
- **Mail-Vorlageneditor**: Der Editor füllt das Fenster, ein Vorschau-Panel folgt jeder Änderung (Desktop- oder Telefonbreite, helles oder dunkles Mailprogramm, Zeile im Posteingang), Versionsverlauf mit Zeilenvergleich und Wiederherstellen (`GET /api/v1/mail/templates/{name}/{culture}/history`), Schutz vor dem Verlassen mit ungespeicherten Änderungen, Sprach-Chips neben dem Titel und eine durchsuchbare Platzhalterliste.
- **Workflow-Designer**: `Coworkee.Workflows.Elsa.Studio` hostet Elsa Studio als eigene App, die sich am Auth-Server anmeldet (Berechtigung `Workflows.Elsa.Manage`) und mit der Elsa-API der API arbeitet; `app.AddElsaStudio<T>()` (oder `AddProjects()` für `*_ElsaStudio`) verdrahtet Client, API-Adresse und den Link zum Designer, `coworkee deploy --without elsastudio` lässt ihn weg. Template-Option `elsaStudio` (`--elsa-studio`). Siehe [Elsa Studio](../modules/workflows-elsa.md#elsa-studio).

### Korrekturen

- Dialoge und Seitenleisten der Bibliothek (Import-Assistent, Kampagnen-Assistent, Theme, Logo, Code-Editor, API-Schlüssel, Workflow-Kommentar) tragen die Startklasse von MudBlazor.Extensions und erscheinen damit erst an ihrer endgültigen Position, statt zuerst kurz in der Mitte aufzublitzen. Eigene Dialoge, die du mit `ShowExAsync` oder `ShowSideSheetAsync` öffnest, setzen `Class="@MudExCss.Classes.Dialog.Initial"` an ihrem `MudDialog`.
- Der Eingang für Genehmigungen zeigt Titel, Details und Frist einer Genehmigung in eigenen Zeilen.
- Apps mit `Coworkee.Workflows.Elsa` behalten die Eigenschaftsnamen ihrer Validierungsfehler (`Brand.Name`); die Elsa-API (FastEndpoints) hatte FluentValidation für jeden Validator auf camelCase umgestellt (`brand.Name`).
- Die Endpunkte von Elsa (`/api/elsa/...`) landen nicht mehr im OpenAPI-Dokument der App: OpenAPI-Snapshots und SDKs von Apps mit `Coworkee.Workflows.Elsa` neu erzeugen.


## 0.0.1.27 (2026-10-16)

### Korrekturen

- Links auf Adressen des Servers (z. B. *Anmelden* am Ende der Einrichtung, `/bff/login`) laden wieder vom Server; seit die Seiten der Bibliothek eine Sammelroute haben, zeigte die App dafür *Not found.*. In Apps ist nichts zu ändern.

## 0.0.1.26 (2026-10-16)

### Breaking Changes

- **`ICoworkeeApi`** hat `GetSystemStatusAsync` dazubekommen; eigene Implementierungen ergänzen es.
- **Seiten der Bibliothek unter eigenem Präfix**: die Admin-Seiten der Bibliothek liegen jetzt unter `/cw/admin/...` statt `/admin/...` (`CoworkeeClientOptions.PagePrefix`, `""` behält die alten Adressen); alte Adressen leiten weiter. Die Seiten der Bibliothek haben keine `@page`-Routen mehr, eine Sammelseite (`/{*Path}`) zeigt sie: eine App mit eigener Sammelseite bekommt eine mehrdeutige Route. Apps ersetzen oder schalten Seiten per Schlüssel ab (`options.Pages`), siehe [Komponenten ersetzen](../ui/customizing.md#seiten-der-bibliothek).
- **Deployments nehmen die Tools des App-Hosts mit**: Ein veröffentlichter App-Host deployt jetzt auch Keycloak, Grafana, WebDataStudio, LibreTranslate und AspireUI. Apps aus einem älteren Template übernehmen drei Dinge aus einem neuen: in `Migrations/Program.cs` den Seed aus `InitialAdmin.From(configuration)` (mit `DemoSeed.ConfigureDeployment`) und `ReconcileKeycloakRealmAsync()`, in `ServiceDefaults/Extensions.cs` den Grafana-Exporter pro Signal mit `GRAFANA_OTLP_PROTOCOL` und in `AppHost.cs` `app.Deploys(...)` für Ollama und `aspireUi.WithCoworkee(app)` für AspireUI. `KeycloakRealm` liegt jetzt in `Coworkee.Contracts.Identity`.
- **Migration nötig**: neue Tabellen `cw.PushSubscriptions` (Notifications), `cw.DataTransferJobs` und `cw.ImportProfiles` (OData, also jede App). Workflows und Webhooks bringen eigene Tabellen mit, wenn eine App sie aufnimmt.
- **`ICoworkeeApi`** hat `AddMailTemplateLanguageAsync`, `GetPushSettingsAsync`, `SavePushSubscriptionAsync` und `DeletePushSubscriptionAsync` dazubekommen; eigene Implementierungen ergänzen sie.
- **`Notification`** erbt jetzt von `AggregateRoot` (sie löst `NotificationCreated` aus, solange Push eingerichtet ist). Das Schema ändert sich dadurch nicht.
- **`IODataClient.ExportAsync` und `ImportAsync` entfernt**: Nutze `IDataTransferApi` (Export der aktuellen Ansicht in drei Formaten, der Import-Assistent). Eigene `IODataClient`-Fakes streichen die beiden Methoden.
- **`ODataImportRegistration`** hat `Create`, `Update` und `Delete` statt `Send`; `AddODataImport` nimmt für Ändern und Rückgängig `(Guid? id, row) => command` und optional einen Lösch-Command.
- **`CoworkeeODataModule` hängt von `CoworkeeBackgroundJobsModule` ab**: Importe und große Exporte laufen als Jobs. Apps aus dem Template haben es bereits (Mailing); Test-Hosts setzen `Coworkee:Jobs:ConnectionStringName` und legen ihre Tabellen vor `UseCoworkee()` an.
- Der *Import*-Button von `CoworkeeDataTable` öffnet den Import-Assistenten, statt sofort eine Excel-Datei hochzuladen; Excel-, CSV- und JSON-Exporte von OData-Sets kommen mit den sichtbaren Spalten vom Server.
- Smartphone-Layout: Apps ergänzen `viewport-fit=cover` im Viewport-Tag von `App.razor` (`coworkee update` erledigt das).
- Neue Endpunkte (Push, Sprachen von Mail-Vorlagen, Import und Export, Verlauf, Workflows, Webhooks): OpenAPI-Snapshots und SDKs neu erzeugen.

`coworkee update` kennt all das (Upgrade-Regeln von 0.0.1.26).

### Neu

- Template-Beispiele: die Befehlspalette bietet *Neues Produkt* (öffnet den Dialog über `/catalog/products?new=true`) und findet Produkte nach Name oder Barcode.
- **Webhooks: Secret erneuern**: ein neues Geheimnis, einmal angezeigt; das alte signiert für `Coworkee:Webhooks:SecretGracePeriod` (24 h) mit, siehe [Webhooks](../modules/webhooks.md). Zwei neue Spalten in `cw.WebhookSubscriptions` (Apps mit Webhooks brauchen eine Migration).
- **Workflow-Chips in Tabellen** laden die Zustände einer Seite in einer Anfrage (`POST /api/v1/workflows/{entityType}/states`, `IWorkflowApi.GetStatesAsync`) statt einer pro Zeile; eigene `IWorkflowApi`-Implementierungen ergänzen die Methode.
- **Schieberegler in Spaltenfiltern**: *zwischen* auf Zahlen- und Datumsspalten regelt zwischen kleinstem und größtem Wert, den der Server meldet; eine Spalte mit Datums- oder Zahlenbereich-Facette (etwa das Bestelldatum der Demo) zeigt deren Histogramm und Regler statt einer Checkbox pro Bucket.
- **Systemstatus**-Seite für die Systemorganisation (`/cw/admin/system-status`): Versionen, Datenbank und ausstehende Migrationen, Outbox, Hintergrundjobs mit den letzten Fehlern, Mail, Echtzeit, Redis, Dateien, Übersetzung und KI; Module und Apps steuern Karten über `ISystemStatusContributor` bei, siehe [Admin-Oberfläche](../features/blazor-ui.md#systemstatus).
- **Workflows und Genehmigungen** (`Coworkee.Workflows`, optional): Zustände und abgesicherte Übergänge pro Entitätstyp, Genehmigungen durch Rollen oder Benutzer mit Frist, Erinnerung und Eskalation, eine Historie, die Komponenten `CoworkeeWorkflow`, `CoworkeeWorkflowActions` und `CoworkeeWorkflowTimeline` und die Seite *Genehmigungen*, siehe [Workflows und Genehmigungen](../modules/workflows.md).
- **Elsa-Workflows** (`Coworkee.Workflows.Elsa`, optional): Elsa Workflows 3 in der API mit Coworkee-Aktivitäten und -Triggern, siehe [Elsa-Workflows](../modules/workflows-elsa.md).
- **Webhooks** (`Coworkee.Webhooks`, optional): Endpunkte pro Organisation bekommen signierte Ereignisse (im Stil von Standard Webhooks) zu Änderungen freigeschalteter Entity-Sets, Workflow-Übergängen und eigenen Ereignissen der App, beschränkt auf das, was der Besitzer des Endpunkts lesen darf; Wiederholungen mit Backoff, Abschalten nach wiederholten Fehlern, Zustellprotokoll, Testereignisse und erneute Zustellung unter `/admin/webhooks`, Schutz vor SSRF. Siehe [Webhooks](../modules/webhooks.md).
- **Import-Assistent** pro Entity-Set: CSV, Excel und JSON, erkannte Kodierung, Trennzeichen und Kopfzeile, Spaltenzuordnung mit Vorschlägen und gespeicherten Zuordnungen, Umwandlung in der Sprache der Datei, Vorschau mit der Validierung der Commands, Anlegen, Ändern per Schlüssel oder beides, Probelauf, Hintergrundjob mit Live-Fortschritt, Datei der fehlerhaften Zeilen und Rückgängig für den letzten Import, siehe [Import und Export](../web/import-export.md). **Admin-Seite** *Import und Export* (`/admin/data-transfer`, Berechtigung `DataTransfer.Use`; gib sie den Rollen, die sie sehen sollen). **Exporte** der aktuellen Ansicht als Excel, CSV oder JSON (`format`, `columns`); mehr als `Coworkee:OData:BackgroundExportRows` Zeilen (10 000) laufen als Job mit Download-Hinweis.
- **Verlauf eines Datensatzes und Wiederherstellen** (`AddEntityHistory`, `History="true"` an `CoworkeeDataTable`, `CoworkeeEntityHistory`): Zeitleiste mit Wort-Diff, Wiederherstellen eines Stands oder eines einzelnen Felds mit Vorschau, gelöschte Datensätze von `ISoftDelete`-Sets kommen zurück, siehe [Audit](../modules/auditing.md#verlauf-eines-datensatzes-und-wiederherstellen).
- **Befehlspalette** (Strg+K): Seiten, Datensätze, Aktionen und der Assistent als Rückfall, erweiterbar mit `AddCommandPaletteProvider`, siehe [Befehlspalette](../ui/command-palette.md).
- **Smartphone-Layout** der Blazor-Shell und der Anmeldeseiten: Überlaufmenü in der App-Leiste, Navigation als Overlay, Bearbeitungsblätter im Vollbild, Facetten-Menüs als Sheets von unten, fixierte Zeilenaktionen, 44-px-Touchziele und Safe Areas, siehe [Smartphones, installierbare App und Push](../ui/phones-and-pwa.md).
- **Installierbare App**: `<Coworkee.Bff.Pwa.CoworkeePwa />` im Head von `App.razor` verlinkt das Web-Manifest in den Farben des Themes; `Coworkee.Bff` liefert Manifest, Service Worker (er speichert nur die Offline-Seite), Icon und Offline-Seite; Update- und Installationshinweise in der App.
- **Web-Push** für die Benachrichtigungen: `options.UsePush()` in Aspire oder `Coworkee:Notifications:Push:PublicKey`/`PrivateKey`; Benutzer schalten es pro Gerät auf der Benachrichtigungsseite ein.
- **Sprachen von Mail-Vorlagen**: *Sprache hinzufügen* unter Administration > Mail-Vorlagen legt eine Variante für eine später eingeschaltete Sprache an, kopiert oder maschinell übersetzt (nur Texte, Tags und Platzhalter bleiben), und entfernt sie wieder, siehe [Mailing](../modules/mailing.md#sprachen-einer-vorlage). Eine Mail in einer Sprache ohne Variante nutzt vor Englisch die Standardsprache (Organisation, System, Sprachen-Seite; `IDefaultLanguage`); die maschinelle Übersetzung steht anderen Modulen als `IMachineTranslator` zur Verfügung.
- **Deployment**: Der App-Host veröffentlicht für Docker Compose, Azure Container Apps oder Kubernetes (öffentliche Adressen, Token-Zertifikate, Setup-Token, der erste Administrator und der Mailserver als Parameter, stabile Volumes, feste Ports); die Tools des App-Hosts (Keycloak mit seinem Realm, Grafana mit den Dashboards der App, WebDataStudio, LibreTranslate, Ollama, AspireUI) kommen mit Volumes und öffentlichen Adressen mit, jedes lässt sich weglassen (`DeployTool`, `coworkee deploy --without`), Mailpit nur mit `--with mailpit`; `coworkee deploy compose|azure|k8s` fragt nach Administrator und Mailserver und listet am Ende jede Adresse und die Anmeldung; Coworkee Studio bietet nach dem Erstellen einer App *Deployen* an; Token-Zertifikate können als Base64 aus einem Secret kommen. Nur Dienste, die Jobs verarbeiten, legen das Hangfire-Schema an. Siehe [Deployment](../hosting/deployment.md).
- **CLI**: `coworkee add`/`remove` schalten Template-Optionen in einer bestehenden App ein oder aus (in deine Dateien zusammengeführt, Konflikte als `.rej`, Optionen in `.coworkee/app.json`), `coworkee update` wendet die Upgrade-Regeln der Versionen dazwischen an, `coworkee db reset|seed|backup|restore`, `coworkee entity import --from db|openapi|csv`, sechs Startvorlagen (`coworkee specs list|show`, `coworkee new --template-spec`), Optionen `--workflows`, `--elsa`, `--webhooks` und `--pwa` (standardmäßig an). MCP-Werkzeuge `add_option` und `import_entities`. Siehe [Kommandozeile](../getting-started/cli.md) und [Startvorlagen](../getting-started/gallery.md).
- **Studio**: eine bestehende App öffnen und ändern, *Meine Apps* (starten, stoppen, Links), *Mit einer Vorlage starten*, Entity-Diagramm (ziehen, Referenzen zeichnen, automatisch anordnen, PNG-Export) und Entity-Import mit Vorschau.
- **Template**: Die Beispielprodukte werden weich gelöscht und zeigen mit Audit ihren Verlauf und kommen zurück; das Dashboard funktioniert mit SQLite (dort kam 500); Demo-Benutzer werden nur in Development angelegt, Migrationen warten auf die Datenbank.
- **`coworkee doctor`** vergleicht in einer App Versionen: dotnet-ef mit EF Core, Aspire-CLI mit dem Aspire-SDK des App-Hosts, coworkee mit `CoworkeeVersion`, und findet belegte Ports der Startprofile, jeweils mit Lösung.
- **Entwicklungsbenutzer bleiben aktuell**: `SyncDevelopmentUsersAsync()` (nur in Development) legt Seed-Benutzer und -Rollen an, die einer früher eingerichteten Datenbank fehlen, und setzt das Passwort der Seed-Benutzer wieder auf das geseedete; der Migrations-Host des Templates ruft es nach `SeedCoworkeeIdentityAsync()` auf, so kommen Änderungen an `DemoUsers.cs` in einer bestehenden Datenbank an.

## 0.0.1.25 (2026-10-15)

Nur Korrekturen: Das Skript des Ladebildschirms ist eine eigene Datei, damit eine strikte CSP es ausführt, und App-Seiten gehen mit `Cache-Control: no-cache, no-store` raus. In Apps ist nichts zu ändern.

## 0.0.1.24 (2026-10-15)

### Breaking Changes

- **`ClientEntityIndicator` entfernt**: Jede Datentabelle einer gespiegelten Menge zeigt den `ClientEntitySwitch` in ihrer Werkzeugleiste (hinzugefügt von `AddCoworkeeClientEntities`). Entferne die Zeilen `<ClientEntityIndicator>` und einen eigenen *Vergleichen*-Knopf aus deinen Seiten.
- **`ODataPicker.Margin`** (und `OptionalODataPicker.Margin`) ist jetzt `Margin?`: Ohne Wert folgt der Picker der Dichte des Themes. Code, der den Wert als `Margin` liest, ergänzt `?? Margin.Dense`.
- **`CliOption`** (Coworkee.Cli.Spec) hat den Konstruktorparameter `DependsOn` dazubekommen, `OptionDependency` die Eigenschaft `Or`: quellkompatibel, aber binär inkompatibel für Werkzeuge, die gegen ein älteres Coworkee.Cli.Spec kompiliert sind.
- **`LoadingScreenDto`** hat den positionalen Parameter `SignedIn` dazubekommen; OpenAPI-Snapshots und SDKs neu erzeugen.
- **Enums in jedem `MudExObjectEdit`** werden mit dem `EnumEditor` dargestellt (lokalisierte Beschriftungen aus `EnumLabels`, `[Flags]` als Mehrfachauswahl der Einzelbit-Mitglieder), nullbare Enums mit dem leerbaren `NullableEnumEditor`. Ein `RenderWith` der Meta gewinnt weiterhin.
- **`--sign-in-media` mit dem Layout `Card`** wird ignoriert (nicht übergeben, nicht kopiert), außer `--loading-screen SignInMedia` zeigt das Medium hinter dem Ladebildschirm.

### Neu

- **Ladebildschirm** in den Farben des Themes mit dem echten Download-Fortschritt, einer freundlichen Fehlermeldung, wenn der Start scheitert, sowie Farbmodus, Theme und Sprache des Benutzers, siehe [Theming](../modules/theming.md#ladebildschirm). **Jede App ergänzt eine Zeile** als erste Zeile von `<body>` in `MyApp.Web/App.razor`: `<Coworkee.Bff.CoworkeeLoadingScreen Logo="coworkee-icon.svg" />`. Der Hintergrund (`Animated`, `SignInMedia`, `Plain`) wird auf der Karte *Anmeldeseite* gewählt, in Aspire mit `options.SignIn.LoadingScreen`, in der CLI mit `--loading-screen`. Die Seite geht jetzt mit `Cache-Control: no-cache, no-store` hinaus; das Logo kommt von `/bff/loading-screen/logo`, das Skript von `/bff/loading-screen/script.js` (kein Inline-Skript, Apps mit strenger Content-Security-Policy laufen weiter).
- **Öffentliche Dateien**: Manager machen eine Datei öffentlich (`PUT /api/v1/files/{id}/public`, Leiste des Dateimanagers, Vorschau); jeder lädt sie über `GET /api/v1/files/public/{id}/{name}`, etwa als Medium der Anmeldeseite oder des Ladebildschirms, gewählt mit *Aus Dateien wählen*. **Das Modell hat `StoredFile.IsPublic` dazubekommen: Apps mit dem Files-Modul fügen eine Migration hinzu.**
- **Seitenmodus von Datentabellen**: `CoworkeeDataTable.PageMode` (`Pager`, `EndlessScroll`) und die Einstellung `DataTable.DefaultPageMode` (global und pro Benutzer), siehe [Datentabelle](../ui/data-table.md).
- **Client-Entity-Schalter** in der Werkzeugleiste gespiegelter Tabellen: Suche im Browser oder auf dem Server, Synchronisierungsstand als Ring, *Vergleichen und das Schnellere wählen* nimmt die schnellere Seite; die Wahl bleibt pro Benutzer erhalten.
- `ImageDataUrlEdit` lädt mit `MudExUploadEdit` hoch; Enums erhalten überall den `EnumEditor` (siehe Breaking Changes).
- Die Texte der MudBlazor.Extensions-Komponenten, die die Bibliothek zeigt (Upload-Feld, Stil- und Cron-Editor, Dateimanager, Dateivorschau), werden in allen Sprachen mitgeliefert; ihre gespeicherten maschinellen Übersetzungen werden beim nächsten Start ersetzt.
- CLI und Studio: Optionen, die eine andere brauchen (`--airworks-packages` ohne `--airworks`, `--ai-provider`, `--ai-model` und `--ai-endpoint` ohne `--ai`, `--backup` außerhalb von Postgres, `--sign-in-media` mit dem Layout `Card`, außer der Ladebildschirm zeigt es, `--sign-in-media-side` ohne Medium), gehen nicht mehr an das Template; die CLI warnt (`coworkee spec validate` ebenso) und das Studio blendet ihre Karten aus. Anmeldemedien eines `Card`-Layouts werden deshalb nicht mehr in die App kopiert. Neu: `coworkee spec show` und eine Spezifikation aus dem Studio, die jedes Bibliotheksmodul mit `true`/`false` nennt; `coworkee options` gibt `Needs:` und die Webseiten der mitgebrachten Produkte aus, die Studio-Karten zeigen Symbole und Öffnen-Knöpfe, siehe [CLI](../getting-started/cli.md#optionen).

## 0.0.1.23 und älter

### Breaking Changes

- **`IMailSender`**: Die Schnittstelle verlangt jetzt `QueueAsync(to, template, model, culture, campaignId, cancellationToken)` mit sechs Parametern. Die Überladung mit fünf Parametern ist eine Default-Methode, die `null` als Kampagne übergibt. Eigene Implementierungen (Testdoubles, Wrapper) implementieren die Methode mit sechs Parametern; Aufrufer bleiben unverändert.
- **`IMailTemplateRenderer`**: hat `RenderContentAsync(content, layout, model, cancellationToken)` dazubekommen, das mit einem bereits aufgelösten Layout rendert (der Kampagnen-Job löst damit Vorlage und Layout einmal pro Lauf auf). Eigene Implementierungen ergänzen die Methode; Aufrufer bleiben unverändert.
- **Positionale Records haben Parameter dazubekommen**: `MailTemplateSummaryDto` (`IsCustom`, `Description`), `MailTemplateDto` (`IsCustom`) und `OutgoingMailDto` (`CampaignId`). Code, der sie positional erzeugt oder dekonstruiert, und generierte Clients brauchen die neuen Parameter. OpenAPI-Snapshots und SDKs neu erzeugen.
- **Entfernt mit der Übernahme von MudEx 9.11.3**: `ChipListEditor`, `CronEditor`, `CronSchedule` und `SettingsFormRules.WrapInColumns`. String-Listen werden als `MudExTagField<string>` dargestellt, `[Cron]` als `MudExCronEdit`; verwende diese direkt.

### Neu

- AirWorks-Client: Chat, Berichte mit Designer, Assistent, die Administrations-Apps (`adminApps`) und Realtime, siehe [AirWorks-Client](../web/airworks.md). Erzeugte Apps nehmen `@coworkee/client` und `@coworkee/airworks` aus den Tarballs im Template; jedes Release veröffentlicht sie zusätzlich auf npm (`--airworks-packages npm`). `coworkee new --template-source` trägt den lokalen Paketordner in die `nuget.config` der App ein. Apps aus der ersten AirWorks-Version: `...adminApps` in `ClientApp/src/main.ts` ergänzen und für die Dateivorschau das MudEx-Hosting des Templates übernehmen (`mudex-webcomponents`, `vite.config.ts`, `.dat`-Content-Type in `Program.cs`).
- Mail-Kampagnen mit Empfänger-Voreinstellungen, gespeicherten Abfragen und manueller Auswahl, geplantem Versand und einer Bestätigungsseite für die Abmeldung, siehe [Mailing](../modules/mailing.md).
- Eigene Mail-Vorlagen mit visuellem und Quelltext-Editor sowie eine `CodeEditor`-Komponente auf Basis von Monaco.
- Änderungen am Entitätsmodell für Kampagnen, Empfängerlisten und eigene Vorlagen: Füge deiner App eine Migration hinzu.
