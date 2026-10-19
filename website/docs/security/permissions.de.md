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
