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

Aus den Definitionen entsteht die Berechtigungsmatrix im Rolleneditor: gruppiert, durchsuchbar, mit Zählern und einem Schalter pro Gruppe.

![Rolleneditor mit Berechtigungsmatrix](../assets/screenshots/role-permissions.png){ .shot }

## Prüfen auf dem Server

| Wo | Wie |
|---|---|
| Ein Request | `[RequiresPermission(CatalogPermissions.Products.Delete)]` am Request-Typ |
| Im Handler | `await permissions.IsGrantedAsync(CatalogPermissions.Products.Edit, ct)` mit `IPermissionChecker` |
| Eine einzelne Ressource | `await permissions.IsGrantedAsync(permission, "Folder", folderId, ct)` |
| Eine Endpunktgruppe | `.RequireAuthorization()` plus das Attribut am Request |
| Ein OData-Set | die Berechtigung, die `AddODataEntity` mitbekommt |

Berechtigungen auf Ressourcen vererben sich entlang einer Hierarchie, die du mit `IResourceHierarchy` beschreibst (etwa Ordner in Ordnern). `IResourceRestriction` entzieht bestimmten Rollen den Zugriff wieder, auch Inhabern einer globalen Berechtigung.

### Wer eine Berechtigung hat

`IPermissionChecker.UsersGrantedAsync(permission, groupId, page, ct)` listet die aktiven Benutzer der Organisation des Aufrufers, die eine Berechtigung haben, etwa für Empfängerlisten oder Auswahlfelder: über eine Rolle (eigene oder die einer ihrer Gruppen), eine eigene Berechtigung oder eine ihrer Gruppen, auch über eine Berechtigung, die sie einschließt; Administratoren immer. Das Ergebnis ist eine Seite von Benutzer-IDs, nach E-Mail sortiert, `PageRequest.Search` sucht in E-Mail und Namen. `groupId` schränkt auf Mitglieder dieser Gruppe und ihrer Untergruppen ein. Wie bei jeder Prüfung gibt eine Gruppe nur ihren eigenen Mitgliedern Rechte, nicht denen ihrer Untergruppen. Alle Rollen zählen, egal welche Rolle ein Benutzer als aktive gewählt hat; berechnete Berechtigungen und Berechtigungen auf einzelnen Ressourcen nicht. Über HTTP mit `Identity.Users.View`:

```http
GET /api/v1/identity/users/granted?permission=Orders.Approve&groupId={id}&page=1&pageSize=25&search=ann
→ { items: [UserDto], totalCount, page, pageSize }
```

Eine unbekannte Berechtigung antwortet `400`, eine unbekannte Gruppe `404`. Eigene `IPermissionChecker`-Implementierungen liefern eine leere Seite, solange sie die Methode nicht implementieren.

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

## Als System handeln

Manchmal muss ein Modul ein Kommando der Bibliothek ausführen, egal was der Aufrufer darf: Das HR-Modul hält die Gruppe eines Teams aktuell, wenn eine Führungskraft das Team bearbeitet, auch wenn sie keine Gruppen verwalten darf. `ISystemDispatcher` sendet so ein Kommando als System:

```csharp
await system.SendAsync(new SetGroupMembers(groupId, memberIds), [IdentityPermissions.Groups.Manage], ct);
```

Das Kommando läuft in der Organisation des Aufrufers, angemeldet ohne Benutzer und mit genau den genannten Berechtigungen (keine Rollen, nichts darüber hinaus), und in einem eigenen DI-Scope, damit die offenen Änderungen des Aufrufers nicht mitgespeichert werden. Seine Audit-Einträge zeigen *system*. `CurrentUserScope.BeginSystem(tenantId, permissions)` tut dasselbe ohne eigenen Scope, etwa in einem Hintergrundjob für eine andere Organisation.

Nutze es erst, nachdem dein eigener Code entschieden hat, dass der Aufrufer die Änderung auslösen darf (`[RequiresPermission]` am eigenen Kommando, eine Prüfung am Datensatz). Reiche nie einen Request weiter, den der Aufrufer gebaut hat: Er könnte damit alles tun, was die genannten Berechtigungen erlauben. Für Arbeit im Namen eines Benutzers handelst du stattdessen als dieser Benutzer (`ImpersonatedUser`).

## Prüfen in der Oberfläche

```razor
@attribute [Authorize(Policy = PermissionPolicy.Prefix + CatalogPermissions.Products.View)]

<PermissionGate Permission="@CatalogPermissions.Products.Create">
    <MudButton OnClick="CreateAsync">New product</MudButton>
</PermissionGate>
```

`PermissionGate` rendert seinen Inhalt nur mit der Berechtigung; Navigationseinträge und Tabellenschaltflächen nehmen eine Berechtigung als Parameter und blenden sich selbst aus. Der Client lädt die wirksamen Berechtigungen des Benutzers nach der Anmeldung.

Die Oberfläche blendet nur aus. Entscheiden tut der Server.

## Berechtigungen nur für den Host

Manche Berechtigungen betreffen die ganze Installation: `Features.Tenants`, `Features.Editions` und `Identity.Clients.Manage` ([Clients und Scopes](clients.md)). Jeder Administrator hat sie, nutzen können sie aber nur die Administratoren der Systemorganisation.

## Rollen seeden

```csharp
seed.Roles.Add(new SeedRole("Product Manager", "Maintains products",
    [CatalogPermissions.Products.View, CatalogPermissions.Products.Create, CatalogPermissions.Products.Edit]));
```

Siehe [Persistenz](../fundamentals/persistence.md#migrationen-und-seed).
