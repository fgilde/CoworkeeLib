# Berechtigungen

Berechtigungen sind Zeichenketten, von den Modulen definiert, an Rollen, Gruppen oder einzelne Benutzer vergeben und auf dem Server wie in der Oberfläche geprüft.

## Definieren

```csharp
internal sealed class CatalogPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(CatalogPermissions.GroupName, "Catalog")
            .Add(CatalogPermissions.Products.View, "View products")
            .Add(CatalogPermissions.Products.Edit, "Edit products", CatalogPermissions.Products.View);
}

services.AddSingleton<IPermissionDefinitionContributor, CatalogPermissionDefinitions>();
```

`Add(name, displayName, implies...)`: Die Namen nach dem Anzeigenamen sind Berechtigungen, die die neue einschließt; eine Rolle mit *Edit products* darf Produkte also auch ansehen. Einschlüsse verketten sich und gelten überall, wo eine Berechtigung geprüft wird. Ein doppelt definierter Name oder eine eingeschlossene Berechtigung, die niemand definiert, beendet den Start mit einer `InvalidOperationException`.

Mehrere Module dürfen Berechtigungen zur selben Gruppe hinzufügen, so wie das Head-Skript `Settings.HeadScript` zur Gruppe des Settings-Moduls: Die Gruppe steht einmal in der Liste, mit dem Anzeigenamen des ersten Moduls, das sie nennt.

Aus den Definitionen entsteht die Berechtigungsmatrix im Rolleneditor: gruppiert, durchsuchbar, mit Zählern und einem Schalter pro Gruppe.

![Rolleneditor mit Berechtigungsmatrix](../assets/screenshots/role-permissions.png){ .shot }

## Rollen seeden

```csharp
seed.Roles.Add(new SeedRole("Product Manager", "Maintains products",
    [CatalogPermissions.Products.View, CatalogPermissions.Products.Create, CatalogPermissions.Products.Edit]));
```

`SeedRole(..., SelectableForRegistration: true)` bietet die Rolle in der [Registrierung](registration.md#rollen-in-der-registrierung) an, `{ IsDefault = true }` gibt sie jedem neuen Benutzer ([Standardrollen](../features/identity.md#besitzer-standardgruppe-und-standardrolle)). Siehe [Persistenz](../fundamentals/persistence.md#migrationen-und-seed).

## Prüfen auf dem Server

| Wo | Wie |
|---|---|
| Ein Request | `[RequiresPermission(CatalogPermissions.Products.Delete)]` am Request-Typ |
| Im Handler | `await permissions.IsGrantedAsync(CatalogPermissions.Products.Edit, ct)` mit `IPermissionChecker` |
| Eine einzelne Ressource | `await permissions.IsGrantedAsync(permission, "Folder", folderId, ct)` |
| Eine Endpunktgruppe | `.RequireAuthorization()` plus das Attribut am Request |
| Ein OData-Set | die Berechtigung, die `AddODataEntity` mitbekommt |

Berechtigungen auf Ressourcen vererben sich entlang einer Hierarchie, die du mit `IResourceHierarchy` beschreibst (etwa Ordner in Ordnern). `IResourceRestriction` entzieht bestimmten Rollen den Zugriff wieder, auch Inhabern einer globalen Berechtigung.

Die wirksamen Berechtigungen eines Benutzers sind die seiner Rollen, der Rollen seiner Gruppen, der Berechtigungen seiner Gruppen und seiner eigenen, mit allem, was sie einschließen; die Systemrolle *Admin* hat jede Berechtigung. Ein Benutzer mit mehreren Rollen kann innerhalb einer davon handeln, der [aktiven Rolle](../modules/realtime.md#aktive-rolle): Solange eine gewählt ist, zählt nur diese, weder die anderen Rollen noch direkt vergebene Berechtigungen. API-Schlüssel und die Liste [Wer eine Berechtigung hat](#wer-eine-berechtigung-hat) ignorieren die aktive Rolle.

### Wer eine Berechtigung hat

`IPermissionChecker.UsersGrantedAsync(permission, groupId, page, ct)` listet die aktiven Benutzer der Organisation des Aufrufers, die eine Berechtigung haben, etwa für Empfängerlisten oder Auswahlfelder: über eine Rolle (eigene oder die einer ihrer Gruppen), eine eigene Berechtigung oder eine ihrer Gruppen, auch über eine Berechtigung, die sie einschließt; Administratoren immer. Das Ergebnis ist eine Seite von Benutzer-IDs, nach E-Mail sortiert, `PageRequest.Search` sucht in E-Mail und Namen. `groupId` schränkt auf Mitglieder dieser Gruppe und ihrer Untergruppen ein. Wie bei jeder Prüfung gibt eine Gruppe nur ihren eigenen Mitgliedern Rechte, nicht denen ihrer Untergruppen. Alle Rollen zählen, egal welche Rolle ein Benutzer als aktive gewählt hat; berechnete Berechtigungen und Berechtigungen auf einzelnen Ressourcen nicht. Über HTTP mit `Identity.Users.View` und `Identity.Roles.View` (die Liste verrät so viel wie die Rollenseite):

```http
GET /api/v1/identity/users/granted?permission=Orders.Approve&groupId={id}&page=1&pageSize=25&search=ann
→ { items: [UserDto], totalCount, page, pageSize }
```

Eine unbekannte Berechtigung antwortet `400`, eine unbekannte Gruppe `404`. Eigene `IPermissionChecker`-Implementierungen liefern eine leere Seite, solange sie die Methode nicht implementieren.

## Prüfen in der Oberfläche

```razor
@attribute [Authorize(Policy = PermissionPolicy.Prefix + CatalogPermissions.Products.View)]

<PermissionGate Permission="@CatalogPermissions.Products.Create">
    <MudButton OnClick="CreateAsync">New product</MudButton>
</PermissionGate>
```

`PermissionGate` rendert seinen Inhalt nur mit der Berechtigung; Navigationseinträge und Tabellenschaltflächen nehmen eine Berechtigung als Parameter und blenden sich selbst aus. Der Client lädt die wirksamen Berechtigungen des Benutzers nach der Anmeldung.

Die Oberfläche blendet nur aus. Entscheiden tut der Server.

## Berechnete Berechtigungen

Manche Rechte folgen aus Daten, nicht aus einer Rolle: „führt mindestens eine Person“, „besitzt ein Projekt“. Eine berechnete Berechtigung vergibt eine definierte Berechtigung, sobald ihre Prüfung true liefert:

```csharp
services.AddComputedPermission(HrPermissions.LeadsPeople, (c, ct) =>
    c.Granted.Contains(HrPermissions.Manage)
        ? Task.FromResult(true)
        : c.Services.GetRequiredService<HrDbContext>().Employees.AnyAsync(e => e.LeaderUserId == c.UserId, ct));
```

Oder eine Klasse, die `IComputedPermission` implementiert, registriert mit `services.AddScoped<IComputedPermission, LeadsPeople>()`. Die Berechtigung muss wie jede andere definiert sein (sie erscheint im Rolleneditor, ein Admin kann sie also auch einer Rolle geben) und schließt ein, was ihre Definition einschließt. Sie gehört überall zu den effektiven Berechtigungen des Benutzers: `[RequiresPermission]`, `IPermissionChecker`, OData-Sets, MCP-Tools, die Berechtigungsliste, die der Client lädt, und damit Navigationseinträge, `PermissionGate` und Policies.

- Die Prüfungen laufen nach den Rollenrechten, einmal pro Request und Benutzer (das Ergebnis gilt für den Request, nicht darüber hinaus), nur für aktive Mitglieder und nur für Berechtigungen, die der Benutzer noch nicht hat. Eine Datenänderung wirkt ab dem nächsten Request; der Client sieht sie, wenn er seine Berechtigungen neu lädt (nach der Anmeldung oder einer Rechteänderung).
- Halte sie bei je einer Abfrage über einen Index: jeder angemeldete Request, der eine Berechtigung prüft, zahlt einmal für alle berechneten Berechtigungen.
- Frag deine Tabellen direkt ab. Ruft eine Prüfung den Permission-Checker oder einen Command mit `[RequiresPermission]` auf, rekursiert sie.
- API-Schlüssel bekommen eine berechnete Berechtigung nur innerhalb ihrer Scopes.

## Einstellungen hinter einer Berechtigung

Eine Einstellung kann zusätzlich zur Regel ihres Scopes (`Settings.Manage` für `Global` und `Tenant`, der Benutzer selbst für `User`) eine eigene Berechtigung verlangen. Sie wird bei der Definition mitgegeben:

```csharp
context.Group("Billing", "Billing")
    .Add("Billing.IbanOverride", "IBAN of the invoices", SettingType.String, [SettingScope.Tenant], permission: BillingPermissions.Bank);
```

Jede Änderung einer solchen Einstellung prüft die Berechtigung: die Einstellungs-Tabs, `PUT /api/v1/settings/{scope}` und die Benutzereinstellungen gleichermaßen. Ein Speichern, das sie ohne die Berechtigung enthält, wird als Ganzes abgelehnt (`403 settings.permission`); Lesen ist nicht eingeschränkt. Das [Head-Skript](../modules/settings.md#head-skript) arbeitet so mit `Settings.HeadScript`.

## Gruppen und Gruppen-Geltungsbereich

Gruppen haben zwei Aufgaben. Als Träger von Berechtigungen wirken sie wie Rollen: Eine Rolle einer Gruppe oder eine Berechtigung an einer Gruppe zählt für ihre **direkten** Mitglieder, nicht für Mitglieder ihrer Untergruppen. Als Einheiten des [Gruppenbaums](../features/identity.md#gruppenbaum-und-geltungsbereich) vergeben sie nichts und entscheiden nur, was ein Benutzer sieht: `IGroupScope` (`Coworkee.Application.Groups`) liefert Filtern die Gruppen, die ein Benutzer erreicht (Mitgliedschaften und ihre Untergruppen), und die, die er in `<GroupScopePicker />` ausgewählt hat. Eine Auswahl schränkt Listen ein, nie den Zugriff, und persönliche API-Schlüssel, MCP, Service-Clients und Jobs ignorieren sie.

| Berechtigung | Wirkung |
|---|---|
| `Identity.Groups.View` | den Gruppenbaum lesen |
| `Identity.Groups.ManageMembers` | Mitglieder hinzufügen, entfernen, verschieben und Besitzer markieren |
| `Identity.Groups.Manage` | Gruppen anlegen, verschieben, löschen; schließt `ManageMembers` ein |
| `Identity.Groups.ScopeAll` | jede Gruppe der Organisation ist erreichbar, für Bearbeiter, die zu keiner Einheit gehören |

Welche Zeilen ein Benutzer sieht, entscheiden die Zeilenfilter der App (`IODataEntityFilter<T>` gegen `IGroupScope.ActiveAsync`); siehe [Welche Gruppen ein Benutzer sieht](../features/identity.md#welche-gruppen-ein-benutzer-sieht).

## Als anderer Benutzer anmelden

Mit `Identity.Users.Impersonate` (schließt `Identity.Users.View` ein, gehört nicht zu `Identity.Users.Manage`) melden sich Support-Mitarbeiter als ein anderer aktiver Benutzer ihrer Organisation an. Das Ziel darf keine Berechtigung haben, die dem Aufrufer fehlt, und jede seiner Gruppen muss in den Gruppen des Aufrufers oder darunter liegen (`Identity.Groups.ScopeAll` hebt das auf); geprüft wird beim Ausstellen des Tickets und bei jeder Autorisierung und Token-Erneuerung, eine spätere Berechtigung für das Ziel beendet die Sitzung also. Details, Tokens und das Ende einer Sitzung: [Als anderer Benutzer anmelden](../features/identity.md#als-anderer-benutzer-anmelden).

Für Berechtigungsprüfungen **ist** die Sitzung das Ziel: `IPermissionChecker` antwortet mit dessen Berechtigungen. Was sich unterscheidet:

- `ICurrentUser.ImpersonatorId` nennt den Impersonator. Das Audit speichert ihn neben dem Benutzer (`AuditEntry.ImpersonatorId`, „angemeldet durch“ auf der Protokollseite, `GET /api/v1/audit?impersonatorId=...`).
- Jobs, Outbox-Nachrichten und Integration-Events aus der Sitzung behalten ihn (`JobEnvelope`, `IntegrationMessage`, `ImpersonatedUser(userId, tenantId, impersonatorId)`), damit auch ihre Änderungen den Impersonator nennen.
- `[NotWhileImpersonating]` an einem Request lehnt ihn mit `403 auth.impersonating` ab, auch in Jobs, die für den Benutzer handeln. Die Bibliothek markiert das Ändern der eigenen E-Mail-Adresse und des Passworts, das Anlegen von API-Schlüsseln, den Export der persönlichen Daten, das Löschen des eigenen Kontos, die Wahl der aktiven Rolle und das Anlegen und Ändern von Webhooks und Bericht-Abonnements. Markiere eigene Requests, die die Zugangsdaten des Benutzers berühren oder für ihn sprechen, genauso.

## Als System handeln

Manchmal muss ein Modul ein Kommando der Bibliothek ausführen, egal was der Aufrufer darf: Das HR-Modul hält die Gruppe eines Teams aktuell, wenn eine Führungskraft das Team bearbeitet, auch wenn sie keine Gruppen verwalten darf. `ISystemDispatcher` sendet so ein Kommando als System:

```csharp
await system.SendAsync(new SetGroupMembers(groupId, memberIds), [IdentityPermissions.Groups.ManageMembers], ct);
```

Das Kommando läuft in der Organisation des Aufrufers, angemeldet ohne Benutzer und mit genau den genannten Berechtigungen (keine Rollen, nichts darüber hinaus), und in einem eigenen DI-Scope, damit die offenen Änderungen des Aufrufers nicht mitgespeichert werden. Seine Audit-Einträge zeigen *system*. `CurrentUserScope.BeginSystem(tenantId, permissions)` tut dasselbe ohne eigenen Scope, etwa in einem Hintergrundjob für eine andere Organisation.

Nutze es erst, nachdem dein eigener Code entschieden hat, dass der Aufrufer die Änderung auslösen darf (`[RequiresPermission]` am eigenen Kommando, eine Prüfung am Datensatz). Reiche nie einen Request weiter, den der Aufrufer gebaut hat: Er könnte damit alles tun, was die genannten Berechtigungen erlauben. Für Arbeit im Namen eines Benutzers handelst du stattdessen als dieser Benutzer: `CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId, impersonatorId))`, mit dem Impersonator einer Sitzung [als anderer Benutzer](#als-anderer-benutzer-anmelden), damit das Audit ihn weiter nennt. Was `ISystemDispatcher` ausführt, läuft als System und nennt keinen Impersonator.

## Berechtigungen nur für den Host

Manche Berechtigungen betreffen die ganze Installation: `Features.Tenants`, `Features.Editions`, `Identity.Clients.Manage` ([Clients und Scopes](clients.md)), `Backups.Manage` ([Backups](../modules/backups.md)) und `Retention.Manage` ([Aufbewahrung](../modules/retention.md)). Jeder Administrator hat sie, sie wirken aber nur in der Systemorganisation; anderswo antworten die Requests mit `403`. Einstellungen mit dem Scope `Global` folgen derselben Regel: `Settings.Manage` ändert sie nur aus der Systemorganisation.
