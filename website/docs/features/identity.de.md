---
title: "Identität und Berechtigungen für ASP.NET Core"
description: "Benutzer, Rollen, Gruppen und Berechtigungen bis auf einzelne Ressourcen, ein OpenIddict-Auth-Server mit BFF, Keycloak oder jeder OIDC-Anbieter, API-Schlüssel."
---

# Identität und Berechtigungen

Die Anmeldung läuft auf einem eigenen Host, einem OpenID-Connect-Server auf Basis von OpenIddict. Der Web-Host arbeitet als Backend for Frontend: Er wickelt den Code-Flow mit PKCE ab, hält die Tokens in einer serverseitigen Sitzung und gibt dem Browser nur ein Cookie. Die API prüft Bearer-Tokens. Ein Access Token bekommt der Browser nie zu sehen.

Eine neue Installation bekommt Mandant, Administrator und Rollen entweder per Seed aus dem Migrationsdienst (so macht es das Template) oder fragt sie beim ersten Start in einem Einrichtungsassistenten ab. Sharemee startet auf diese Weise.

![Rolleneditor mit der Berechtigungsmatrix, nach Modulen gruppiert](../assets/screenshots/role-permissions.png){ .shot loading=lazy }

## Wer was darf

Berechtigungen sind Strings, die die Module festlegen. Vergeben werden sie an Rollen, Gruppen oder einen einzelnen Benutzer, und der Rolleneditor zeigt sie als Matrix mit einem Schalter pro Gruppe. Ein Request sagt mit `[RequiresPermission]`, was er braucht, und der Dispatcher prüft das, bevor der Handler läuft.

Freigaben gehen auch auf eine einzelne Ressource, etwa einen Ordner, und vererben sich entlang einer Hierarchie, die du mit `IResourceHierarchy` beschreibst. Blazor-Seiten blenden Schaltflächen und Menüeinträge aus, die der Benutzer nicht nutzen darf, aber das ist Kosmetik. Entscheiden tut der Server.

Auf der Seite eines Benutzers bearbeiten Administratoren alles, von der E-Mail-Adresse bis zur Sperre und zum Zurücksetzen der Zwei-Schritt-Verifizierung. *Überall abmelden* und *Sperren* greifen sofort: Tokens enthalten einen Hash des Security Stamps, die API vergleicht ihn bei jedem Request, und mit Redis erfahren es die anderen Instanzen ohne Verzögerung.

## Gruppenbaum und Geltungsbereich

Gruppen bilden pro Mandant einen Baum und können so Organisationseinheiten sein: Kunden, Tochtergesellschaften, Filialen. Eine Gruppe hat eine optionale übergeordnete Gruppe und einen `Code`, einen Pfad aus 5-stelligen Segmenten (`00001.00003`), den nur die Handler pflegen. Du legst eine Gruppe unter einer übergeordneten an, verschiebst sie (`PUT /api/v1/identity/groups/{id}/parent`, der Teilbaum zieht mit) oder löschst sie, womit auch ihre Untergruppen verschwinden. Namen sind unter Geschwistern eindeutig, die Tiefe ist auf 16 begrenzt. Gruppen einer bestehenden App werden zu Wurzeln; der Migrationshost vergibt ihre Codes beim nächsten Lauf.

Benutzer sind Mitglied in beliebig vielen Gruppen. **Rollen und Berechtigungen hängen nicht an Gruppen**: Eine Gruppe vergibt als Einheit nichts, sie bestimmt nur, was ein Benutzer sieht. Mitglieder werden mit `Identity.Groups.ManageMembers` (in `Identity.Groups.Manage` enthalten) hinzugefügt, entfernt oder alle auf einmal in eine andere Gruppe verschoben. `Identity.Groups.ScopeAll` („In jeder Gruppe arbeiten") macht jede Gruppe des Mandanten zugänglich, für Operatoren ohne eigene Einheit; Administratoren mit allen Berechtigungen haben sie. Die Baum-Endpunkte (`/groups/children`, `/groups/tree`) akzeptieren `Groups.View` oder `ScopeAll`.

Auf der Gruppenseite lädt der Baum träge, mit Suche, Zählern und den Reitern Allgemein, Mitglieder und Rollen. `?group={id}` springt direkt zu einer Gruppe. Apps ergänzen eigene Reiter:

```csharp
services.AddGroupDetailTab<OrganisationCodes>("Organisation codes", ShipmentPermissions.Shipments.View);
// die Komponente hat [Parameter] public Guid GroupId { get; set; }
```

### Welche Gruppen ein Benutzer sieht

`IGroupScope` (Namespace `Coworkee.Application.Groups`, ohne Identity-Referenz nutzbar) antwortet pro Request:

| Methode | Ergebnis |
|---|---|
| `MembershipsAsync` | die Gruppen, in denen der Benutzer direkt Mitglied ist |
| `AccessibleAsync` | Mitgliedschaften samt aller Untergruppen; mit `ScopeAll` jede Gruppe des Mandanten |
| `SelectedAsync` | die Auswahl des Benutzers, begrenzt auf das Zugängliche |
| `ActiveAsync` | die Auswahl samt Untergruppen; `AccessibleAsync`, wenn nichts ausgewählt ist |
| `NodesAsync`, `AncestorsAsync` | `GroupNode(Id, ParentId, Code, Name)` von Gruppen oder der Pfad über einer Gruppe (Wurzel zuerst) |

Die Auswahl im Picker der App-Leiste speichert der Server pro Benutzer (`cw.GroupSelections`): Nichts ausgewählt heißt alles, was der Benutzer erreicht; einzelne Gruppen ausgewählt heißt diese samt Untergruppen. Eine Auswahl erweitert nie den Zugriff, und eine veraltete (Mitgliedschaft entfernt) wird ignoriert. Sie gilt nur für interaktive Anmeldungen; persönliche API-Schlüssel, MCP, Service-Clients und Hintergrundjobs sehen `AccessibleAsync`. Ohne Benutzer sind alle Listen leer; Ausnahmen (etwa eine Berechtigung „alles sehen") regelt die App.

Apps schränken ihre Zeilen mit einem Filter ein, der einmal gegen `ActiveAsync` geschrieben wird:

```csharp
internal sealed class ShipmentFilter(IGroupScope scope) : IODataEntityFilter<Shipment>
{
    public async Task<IQueryable<Shipment>> ApplyAsync(IQueryable<Shipment> query, CancellationToken ct)
    {
        var ids = await scope.ActiveAsync(ct);
        return query.Where(s => EF.Parameter(ids).Contains(s.GroupId));
    }
}
```

`EF.Parameter` ist auf SQL Server wichtig, das bei einem einfachen `Contains` mehr als 2100 Parameter ablehnt.

Die Auswahl bestimmt, was **Listen** zeigen, nicht, was ein Benutzer öffnen darf. Die Bibliothek schränkt Collections, `$count`, Facetten, Suche, Export, `QueryData`, Client-Entitäten und Berichte nach der Auswahl ein. Lesezugriffe per Schlüssel, die Prüfungen für Anhänge und Social-Funktionen (`VisibleIdsAsync`, also auch die Tag-Verknüpfungszähler), Verlauf, Import-Abgleich, Workflow-Sichtbarkeit und Webhooks laufen ohne Einschränkung, ein Link auf einen Datensatz einer anderen Einheit öffnet sich also weiter. Eigener Code, der Zugriff statt Auflistung meint, wird in `using (GroupScope.Unnarrowed())` gepackt. In der Oberfläche gestartete Exporte halten die Auswahl zum Zeitpunkt der Anforderung fest und der Job läuft unter `GroupScope.Selection(ids)`; sind bis dahin alle festgehaltenen Gruppen veraltet, fällt die Datei auf alles Zugängliche zurück. Der Client-Entity-Spiegel folgt der Auswahl (`IClientEntityPartitionPart`), eine Änderung synchronisiert also die Browser-Kopie neu.

`<GroupScopePicker />` setzt du selbst in die App-Leiste; das Template-Layout enthält ihn nicht, und er blendet sich für Benutzer ohne zugängliche Gruppen aus. Datentabellen laden bei einer Änderung der Auswahl neu (`GroupScopeState`), und `IClientDataCache` ist die Schnittstelle für eigene clientseitige Caches.

Der Gruppenimport nimmt `Name`, `Description` und `ParentPath` („Kunde A/Hamburg"); ein unbekanntes Elternteil ist eine Fehlerzeile, fehlende Eltern werden nicht angelegt. Ein `/` im Namen ist per Pfad nicht ansprechbar, leere Pfadsegmente werden ignoriert, eine leere Beschreibung löscht eine vorhandene nicht, und ein Probelauf kann unbekannte Pfade nicht melden (der echte Lauf schon). Das Users-OData-Set liefert `Groups` für Filter wie `Groups/any(m: m/GroupId eq {id})`.

!!! warning "Echtzeit und Einheiten als Kunden"
    `[Realtime]` sendet `type:X` weiterhin an alle mit der Ansichts-Berechtigung. Sind Einheiten Kunden in einem Mandanten, sehen Clients anderer Einheiten die IDs und geänderten Eigenschaftsnamen von Datensätzen, die sie nicht öffnen dürfen (und laden neu). Themen pro Gruppe sind ein Folgeschritt.

Workflow-Genehmiger, Mail an eine Gruppe, Ressourcenberechtigungen und Gruppenrollen verwenden die **direkte** Mitgliedschaft, nicht den Baum.

## Keycloak und andere Anbieter

Jeder OpenID-Connect-Anbieter kann Benutzer anmelden, jeder bekommt einen Abschnitt unter `Coworkee:Auth:External:Providers`. Mit einem bestehenden Konto verknüpft wird eine externe Anmeldung nur, wenn der Anbieter die E-Mail-Adresse als bestätigt meldet (oder mit `TrustEmail` konfiguriert ist). In der Entwicklung startet `options.UseKeycloak()` im Aspire-App-Host ein Keycloak mit einem Realm, der dieselben Benutzer und Passwörter hat wie die Anwendung.

Ob man sich per Passwort, über einen Anbieter oder auf beiden Wegen anmeldet, regelt die Einstellung `Auth.LoginMode`, auch im laufenden Betrieb. Die Selbstregistrierung (aus, solange `Account.AllowRegistration` nicht gesetzt ist) kann eine Freischaltung durch einen Administrator verlangen, dazu eine bestätigte Adresse, erlaubte Adressmuster und hochgeladene Dokumente. Auch die Passwortregeln sind Einstellungen, bis hin zu einem Verlauf von 24 Passwörtern, und jedes Passwortfeld nennt die gerade geltenden Regeln.

## Schlüssel und persönliche Daten

Skripte und KI-Clients nutzen persönliche API-Schlüssel: `cwk_` plus 64 Hex-Zeichen, einmal angezeigt, als SHA-256-Hash gespeichert, 7 bis 365 Tage gültig. Ein Schlüssel hat nie mehr Rechte als sein Benutzer im Moment; nimmst du dem Benutzer eine Rolle weg, ist sie beim nächsten Request auch beim Schlüssel weg.

Module, die Daten über eine Person speichern, implementieren `IPersonalDataContributor`. Über den Reiter *Datenschutz* der Kontoseite lädt man dann alles als `personal-data.json` herunter oder löscht das Konto.

**Stand:** Version 0.0.1.x, Namen können sich hier zwischen Releases noch ändern; was anzupassen ist, steht im [Änderungsprotokoll](../reference/changelog.md).

Referenz: [Anmeldung und Keycloak](../security/authentication.md), [Berechtigungen](../security/permissions.md), [Registrierung und Anmelderegeln](../security/registration.md), [Clients, Sitzungen und Passwortregeln](../security/clients.md), [Anmeldeseiten](../security/sign-in-pages.md), [API-Schlüssel](../security/api-keys.md), [Datenschutz](../security/privacy.md).
