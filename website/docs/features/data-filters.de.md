---
title: "Gestaffelte Datenfilter für ASP.NET-Core- und Blazor-Apps"
description: "Administratoren legen pro Entity-Set Filter auf vier Ebenen fest (System, Mandant, Gruppe, Rolle), die Benutzer nie sehen: Zusammenspiel, Details und verknüpfte Sets, Echtzeit und Admin-Oberfläche."
---

# Gestaffelte Datenfilter

Administratoren legen pro Entity-Set Filter fest, die immer gelten und die Benutzer nie sehen. Sie liegen auf vier Ebenen: **System**, **Mandant**, **Gruppe** und **Rolle**. Die eigene Filterung, Facetten und Zähler eines Benutzers arbeiten auf dem, was übrig bleibt. Nichts verrät dem Benutzer, dass Zeilen fehlen: Zähler, Facetten, Suche und Paging werden auf den gefilterten Zeilen berechnet.

Die Filter gelten überall, wo die Bibliothek entscheidet, welche Zeilen ein Benutzer sehen darf: OData-Listen und `$count`, `$expand`-Ziele, Client-Entities (lokale Kopien, Snapshots und Deltas), globale Suche, dynamische Berichte und Exporte. Ist für ein Set kein Filter eingerichtet, gibt es kein zusätzliches `Where`; das festzustellen kostet einen gecachten Nachschlag der Filter des Mandanten (bei kaltem Cache und innerhalb einer Transaktion aus der Datenbank gelesen).

## Die vier Ebenen

| Ebene | Gilt für | Geschrieben mit |
|---|---|---|
| System | jeder Mandant | `DataFilters.ManageSystem` (nur im System-Mandanten) |
| Mandant | der eigene Mandant | `DataFilters.ManageTenant` |
| Gruppe | Mitglieder einer Gruppe (und ihrer Untergruppen) | `DataFilters.ManageGroups` |
| Rolle | Benutzer, die in einer Rolle arbeiten | `DataFilters.ManageRoles` |

`DataFilters.Bypass` („alle Zeilen sehen“) schaltet für den Inhaber die Ebenen Mandant, Gruppe und Rolle ab, und die Systemebene ebenfalls, wenn er zum System-Mandanten gehört. Die eingebaute Rolle Admin hat jede definierte Berechtigung, also haben die Administratoren jedes Mandanten Bypass: Sie sehen die Daten ihres Mandanten ohne dessen eigene Filter, die Systemfilter gelten für sie aber weiter; nur die Administratoren des System-Mandanten sehen die ungefilterten Daten. Filter werden als `QueryGroup` des [`QueryBuilder`](data.md) gespeichert und auf dem Server in `$filter` übersetzt; das Speichern prüft sie gegen das Modell des Sets und lehnt einen Filter ab, der eine Eigenschaft verwendet, die der speichernde Administrator nicht lesen darf (`HideUnless`).

## So greifen die Ebenen ineinander

System, Mandant und die aktiven Rollen des Benutzers werden mit **UND** verknüpft. Aktive Rollen sind die Rolle, in der der Benutzer arbeitet (wenn der Rollenwechsel an ist und eine Rolle gewählt wurde), sonst alle Rollen des Benutzers (direkt und über Gruppen), genau wie der Berechtigungsprüfer sie sieht.

Die Gruppenebene **schränkt nur die Zeilen ein, die über eine Gruppe kommen**. Bei einem Set mit `IGroupRowSource<T>` (siehe unten) ist eine Zeile sichtbar, wenn

```text
NOT rowsOf(aktive Gruppen)
OR rowsOf(Gruppen ohne Filter)
OR (rowsOf(Gi) AND fi ...)
```

Also: Zeilen, die über keine der aktiven Gruppen zum Benutzer kommen, bleiben dem eigenen Scope der App überlassen, Zeilen über eine Gruppe ohne Filter gehen durch, und Zeilen über eine Gruppe mit Filtern müssen deren Filter bestehen. Der Filter einer Gruppe schneidet nie die Zeilen einer anderen Gruppe ab. Filter vererben sich nach unten: Zeilen, die über eine Gruppe kommen, müssen die Filter dieser Gruppe **und** aller Vorfahren bestehen (UND), eine Untergruppe zeigt also nie mehr, als ihre Obergruppe erlaubt.

Die Gruppenebene entfällt für Benutzer mit `Identity.Groups.ScopeAll`, für Benutzer, die die Zeilenquelle der App als „sieht alles“ meldet, und für Benutzer ohne Gruppen. Sets ohne `IGroupRowSource<T>` nutzen die Regel „sichtbar, wenn der wirksame Filter mindestens einer Gruppe des Benutzers die Zeile durchlässt“; eine Gruppe ohne Filter lässt alles durch.

**Beispiel.** Ein Systemfilter auf `CalculatedShipment` behält die Status Complete, Delivered und InProcess. Die Gruppe *Alfa Laval* behält zusätzlich nur Sendungen aus Hamburg oder Bremen. Die Rolle *Whatever* behält nur Completed. Ein Mitglied von Alfa Laval, das in dieser Rolle arbeitet, sieht abgeschlossene Sendungen von Alfa Laval aus Hamburg oder Bremen und filtert weiter wie bisher. Ein Kollege in einer anderen Gruppe verliert wegen des Alfa-Laval-Filters keine Zeile.

## Details und Zugriff über den Schlüssel

Jeder Filter hat den Schalter **Gilt auch für Details**, standardmäßig an. An: Ein Link oder Schlüsselzugriff auf einen herausgefilterten Datensatz antwortet „nicht gefunden“. Aus: Die Liste blendet den Datensatz aus, der Schlüsselzugriff liefert ihn aber noch. Code, der einen einzelnen Datensatz über den Schlüssel liest, läuft in `DataFilterScope.ByKey()`; die OData-Pipeline macht das für Schlüsselanfragen und `IODataQueries.GetByKeyAsync` für Schlüsselzugriffe im Prozess, und was ein Datensatz expandiert, ist weiter eine Liste und bleibt gefiltert.

```csharp
using (DataFilterScope.ByKey())
{
    var shipment = await visible.FirstOrDefaultAsync(s => s.Id == id, ct);
}
```

## Verknüpfte Sets

Sets über dieselben Daten mit unterschiedlichen Eigenschaften (`Shipment` und `CalculatedShipment`) werden auf der Admin-Seite über ihren gemeinsamen Schlüssel verknüpft: *(Set, Eigenschaft) → Zielset*. Der Filter wird einmal geschrieben, auf dem Set, das die Eigenschaften hat. Ein verknüpftes Set wird über den Schlüssel eingeschränkt: Eine `Shipment`-Zeile ist nur sichtbar, wenn die `CalculatedShipment`-Zeile mit demselben Schlüssel die Filter des Ziels besteht (und dessen eigene Verknüpfung, Ketten funktionieren also). Beide Sets müssen im selben `DbContext` liegen, die Eigenschaft muss den Typ des Schlüssels des Ziels haben, und eine Kette von Verknüpfungen darf nicht zu einem ihrer Sets zurückführen; das Speichern lehnt jeden dieser Fälle ab. Eine Verknüpfung, deren Ziel, Eigenschaft oder Schlüssel nicht mehr existiert, schließt sicher (keine Zeilen).

## Ungültige Filter schließen sicher

Ein gespeicherter Filter, der nicht mehr übersetzt wird (eine Eigenschaft wurde umbenannt oder entfernt), bedeutet für seine Ebene und sein Set „keine Zeilen“, und die Admin-Seite zeigt ihn mit Grund als ungültig an. Für Benutzer wirft er nie und lässt nie Zeilen durch.

## Welche Zeilen über eine Gruppe kommen: `IGroupRowSource<T>`

Die Bibliothek kann nicht wissen, wie eine App Zeilen mit Gruppen verbindet; deshalb registriert die App pro Set, das über Gruppen erreicht wird, eine Quelle:

```csharp
[RegisterAs(typeof(IGroupRowSource<Shipment>))]
internal sealed class ShipmentRowsOfGroups(IOrgCodes codes, IPermissionChecker permissions) : IGroupRowSource<Shipment>
{
    public async Task<Expression<Func<Shipment, bool>>> RowsOfAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken ct)
    {
        var org = await codes.OfGroupsAsync(groupIds, ct);
        return s => org.Contains(s.OrganisationCode);
    }

    public Task<bool> SeesAllAsync(CancellationToken ct) => permissions.IsGrantedAsync("Shipments.ViewAll", ct);
}
```

`IOrgCodes` und `OfGroupsAsync` stehen für den eigenen Dienst der App, der Gruppen auf die Codes abbildet, die ihre Zeilen tragen. `RowsOfAsync` liefert ein Prädikat, das EF Core übersetzt, für die Zeilen genau dieser Gruppen (nicht ihrer Untergruppen; Untergruppen, die der Benutzer erreicht, reicht die Ebene mit ihren eigenen Filtern durch). Ohne Quelle gilt für ein Set die Regel „Filter irgendeiner Gruppe“. Der Gruppenteil verneint das Prädikat „kommt über eine Gruppe“ (`NOT rowsOf(aktive Gruppen)`), deshalb zählen leere Gruppenverknüpfungen (null), für Benutzer, für die die Gruppenebene gilt, und bei Sets mit Gruppenfiltern. Mit der EF-Standard-Null-Semantik gilt eine Zeile mit leerer Gruppenverknüpfung als „nicht über eine Gruppe“ und bleibt dem eigenen Scope der App überlassen; mit `UseRelationalNulls(true)` bleibt `NOT(NULL)` NULL, die Zeile fällt also heraus (sicher geschlossen).

## Rollenfilter brauchen `GetRoleIdsAsync`

Rollenfilter gelten für die aktiven Rollen des Benutzers, gelesen über `IPermissionChecker.GetRoleIdsAsync`. Ein eigener Berechtigungsprüfer, der sie nicht implementiert, liefert keine Rollen, **Rollenfilter greifen** für seine Benutzer also **nicht**. Wer den Prüfer ersetzt, implementiert sie mit.

## Vorschau

Der Editor zeigt, wie viele Zeilen ein Filterentwurf durchlässt, bevor er gespeichert wird. `POST /api/v1/data-filters/preview` nimmt die Felder eines Speicherns und speichert nichts:

```http
POST /api/v1/data-filters/preview
{ "entitySet": "Orders", "layer": "Tenant", "layerKey": null, "query": "{...}" }

200 { "passing": 4, "total": 10, "timedOut": false }
```

`total` zählt die Zeilen, die der Bearbeiter sieht: durch die eigenen Zeilenfilter der App und jeden Datenfilter, der ihn bindet, außer dem bearbeiteten (dessen gespeicherter Fassung; ein neuer Entwurf hat keine), in allen seinen Gruppen (die Gruppenauswahl grenzt nicht ein). Ein Inhaber von **Alle Zeilen sehen** zählt im Systemmandanten jede Zeile, anderswo die Zeilen, die die Systemfilter übrig lassen. `passing` zählt davon die, die auch den Entwurf passieren. Die Vorschau zählt also innerhalb dessen, was Sie sehen: Ein Mandantenadministrator, den Systemfilter binden, oder ein Gruppen- oder Rollenverwalter erfährt nie, wie viele Zeilen die anderen Ebenen verbergen. Die Anfrage braucht die Berechtigung ihrer Ebene, die Leseberechtigung auf das Set und dieselben Eigenschaftsprüfungen wie das Speichern (400 bei unbekannten oder geschützten Eigenschaften). Nach `CoworkeeODataOptions.DataFilterPreviewTimeout` (Standard 5 Sekunden) bricht das Zählen ab und antwortet `{ "passing": null, "total": null, "timedOut": true }`. Im Code schaltet `DataFilterScope.Off()` die Datenfilter für den aktuellen Ablauf aus; die eigenen Zeilenfilter der App gelten weiter.

## Übersicht

`GET /api/v1/data-filters/overview` sagt pro Entity-Set, wie viele Filter die Ebene des Administrators hält und ob das Set verknüpft ist:

```http
GET /api/v1/data-filters/overview

200 { "layer": "Tenant", "tenantName": "Acme", "sets": [ { "entitySet": "Orders", "layerFilters": 1, "linked": false } ] }
```

Die Ebene ist `System` im Systemmandanten (braucht `DataFilters.ManageSystem`), sonst `Tenant` (braucht `DataFilters.ManageTenant`). Verknüpfungen meldet nur der Systemmandant.

## Echtzeit

Jede Änderung lädt offene Tabellen neu und gleicht lokale Kopien ab; die Partition einer Client-Kopie enthält einen Fingerabdruck aller Filter des Mandanten des Benutzers und der Systemfilter (Ids und Versionen, Verknüpfungen eingeschlossen), seiner aktiven Rollen, die einen Rollenfilter tragen, und, wenn es Gruppenfilter gibt, seiner aktiven Gruppen; ein geänderter Filter, ein Rollenwechsel oder eine andere Gruppe verwirft also die Kopie und holt die sichtbaren Zeilen neu. Ohne Filter bekommt die Partition nichts dazu. Änderungen senden Signale ohne Nutzdaten und ohne Akteur: Entities implementieren `IHasRealtimeSignals`, um die Topics zu nennen, der Ereignistyp ist `RealtimeEventTypes.Signal`. Ein Signal trägt eine leere Nutzlast (weder Entity-Name noch Id noch Filterinhalt) und keinen Akteur. Mandanten-, Gruppen- und Rollenfilter signalisieren auf `tenant:data-filters` (nur der eigene Mandant), Systemfilter und Verknüpfungen auf `global:data-filters`. „Signal“ ist ein reservierter Ereignisname: Eine App darf ihn nicht für eigene Ereignisse verwenden.

## Admin-Oberfläche

- **Datentypen** listet jedes Entity-Set mit seinen Client-Entity-Einstellungen. Ein Suchfeld grenzt die Liste nach Set- oder Typname ein, zwei Chips zeigen nur Sets mit lokaler Kopie oder nur Sets mit Datenfiltern. Jede Zeile endet mit denselben Aktionen: Stift (lokale Kopie), Zurücksetzen und ein Filter-Symbol, das die Filter der Zeile darunter öffnet. Das Filter-Symbol ist eingefärbt und trägt eine Marke (Filter Ihrer Ebene plus Verknüpfung), wenn das Set gefiltert ist, so sieht man auf einen Blick, welche es sind. Das Feld heißt im Systemmandanten „Filter für alle Mandanten“ (Systemebene und Verknüpfungseditor), in jedem anderen Mandanten „Filter für Mandant Acme“ (nur die Ebene dieses Mandanten). Gruppen- und Rollenfilter werden auf der Gruppen- und der Rollenseite bearbeitet. Die Zahlen folgen Änderungen live.
- Der Reiter **Datenfilter** auf der Gruppenseite und der Rollenseite bearbeitet die Filter dieser Gruppe oder Rolle.
- Der Filtereditor ist der `QueryBuilder`: Regeln und Gruppen per Drag and Drop in andere Gruppen oder Positionen ziehen (oder über das Knotenmenü: hoch, runter, in eine Gruppe, aus einer Gruppe), eine Gruppe zwischen UND und ODER umschalten, angehakte Bedingungen in eine neue Gruppe einpacken, eine Gruppe auspacken. Auspacken gibt es nur, wenn die Bedeutung gleich bleibt: Die Logik der Gruppe entspricht der ihrer übergeordneten Gruppe, oder sie hat höchstens ein Kind und ist nicht negiert.
- Unter dem Titel des Editors steht, wen der Filter nicht bindet: Inhaber von **Alle Zeilen sehen** (die Administratoren des Mandanten; bei Systemfiltern die des Systemmandanten). Unter dem Builder zeigt eine Live-Vorschau zum Beispiel „4 von 1.200 Zeilen, die Sie sehen, passieren diesen Filter“. Sie wird nur beim Bearbeiten berechnet, 400 ms nach der letzten Änderung, über den Vorschau-Endpunkt (eine Filterliste, die sich öffnet oder nach einer Änderung anderswo neu lädt, zählt nichts), zeigt „Vorschau nicht verfügbar: Das Zählen hat zu lange gedauert.“ bei einem Zeitlimit und keine Zeile, solange der Entwurf ungültig ist.

Lesen und Schreiben braucht die Berechtigung der Ebene; die Seite und der Navigationseintrag erscheinen für jeden mit einer der `DataFilters.Manage*`-Berechtigungen.

## Gruppen- und Rollenfilter

Der Gruppenbaum und die Rollentabelle zeigen, welche Gruppen und Rollen Datenfilter haben, man muss sie nicht einzeln öffnen. Eine Gruppe oder Rolle mit Filtern hat ein eingefärbtes Filter-Symbol mit Marke (Anzahl der Filter); die anderen zeigen nichts, ein Baum mit Tausenden Gruppen bleibt also ruhig. Das Symbol öffnet den Reiter **Datenfilter** der Gruppe oder Rolle (der Link `?group={id}&tab=data-filters` auf der Gruppenseite, `/admin/roles/{id}?tab=data-filters` auf der Rollenseite). Der Chip **Nur mit Datenfiltern** über dem Baum listet nur die gefilterten Gruppen, flach und mit ihrem Pfad benannt („Kunde A / Hamburg“); über der Rollentabelle grenzt er die Tabelle auf die gefilterten Rollen ein. Die Seite lädt die Filter einmal (`GetDataFiltersAsync(layer: Group)` oder `Role`, gezählt nach `LayerKey`), nicht einmal pro Knoten, und lädt sie neu, wenn das Echtzeit-Thema `DataFilterTopics.TenantChanged` feuert; geöffnete Baumknoten bleiben offen. Symbol und Chip brauchen `DataFilters.ManageGroups` oder `DataFilters.ManageRoles`.

Filter gehen mit ihrer Gruppe oder Rolle: Das Löschen einer Gruppe (samt der Gruppen darunter) oder einer Rolle löscht ihre Gruppen- oder Rollenfilter im selben Speichern, und alle bekommen das Signal `TenantChanged`. Der Seed-Contributor `DataFilterOrphansSeed` entfernt die Waisen, die ältere Versionen hinterlassen haben (Filter, deren Gruppe oder Rolle nicht mehr existiert); er läuft bei jedem Seed-Lauf und ändert nichts mehr, sobald sie weg sind.

## Gruppenbezogene Verwaltung

Mit eingeschalteter [gruppenbezogener Verwaltung](../security/permissions.de.md#gruppenbezogene-verwaltung) folgen die Gruppen- und die Rollenebene der Reichweite des Verwalters (`IDataFilterReach` in `Coworkee.Application.Groups`, vom Identity-Modul aus `AdminReach` gebildet):

| Aufrufer | Gruppenfilter | Rollenfilter |
|---|---|---|
| mandantenweit: Administratoren, Inhaber von `Identity.Groups.ScopeAll`, das System oder die Einstellung aus | jede Gruppe des Mandanten | jede Rolle des Mandanten |
| beschränkt, mit `DataFilters.ManageGroups` oder `DataFilters.ManageRoles` | listet die der Gruppen in seiner Reichweite; speichert, prüft in der Vorschau und löscht die der Gruppen **unter** seinen eigenen obersten Gruppen | keine: `403 data_filters.forbidden` |

- **Filter Ihrer eigenen obersten Gruppen legt ein übergeordneter Administrator fest.** Ihre obersten Gruppen sind Ihre Mitgliedschaften (ohne die Standardgruppe), über denen keine andere Mitgliedschaft liegt. Ihre Filter grenzen ein, was Sie sehen; Sie lesen sie also, können sie aber weder speichern noch in der Vorschau prüfen noch löschen (`403 data_filters.forbidden`), sonst könnten Sie Ihre eigene Sicht erweitern. Eine Gruppe unter einer Ihrer obersten Gruppen, in der Sie Mitglied sind (etwa eine selbst angelegte), ist eine gewöhnliche Untergruppe. Auch ein Mitglied jeder Gruppe ist hier beschränkt; mandantenweit sind nur die Aufrufer der ersten Zeile.
- Gruppen außerhalb der Reichweite fehlen in `GET /api/v1/data-filters`; Speichern oder Vorschau für eine davon antwortet `403 data_filters.forbidden`, mit demselben Code und derselben Meldung wie für eine Id, die keine Gruppe bezeichnet, Ids lassen sich also nicht ausprobieren. Das Löschen eines Filters einer solchen Gruppe antwortet `404 data_filters.not_found`.
- Ein beschränkter Verwalter, dessen einzige Gruppe die Standardgruppe ist, sieht keine Gruppenfilter (leere Liste) und speichert keine.
- **Schlüssel müssen existieren:** Ein mandantenweiter Aufrufer, der einen Gruppen- oder Rollenfilter speichert oder in der Vorschau prüft, dessen `LayerKey` keine Gruppe (`IGroupScope.NodesAsync`) oder Rolle des Mandanten bezeichnet, bekommt `400` mit dem Feld `layerKey` („Diese Gruppe oder Rolle gibt es nicht.“).

**Was ein beschränkter Gruppenverwalter auf den Admin-Seiten sieht.** Die Gruppenseite lädt seine eigenen obersten Gruppen einmal (`GET /api/v1/identity/groups/locked`, `ICoworkeeApi.GetLockedGroupIdsAsync`: ihre Ids, leer für mandantenweite Aufrufer; `Identity.Groups.View`) und erneut, wenn sich Gruppen ändern (Echtzeit). `GroupDto.Locked` trägt dieselbe Angabe in der Gruppenliste.

- Auf seinen eigenen obersten Gruppen zeigt der Reiter **Datenfilter** die Filter als Text unter dem Hinweis „Von einem übergeordneten Administrator festgelegt“ („Keine Datenfilter.“, wenn es keine gibt), ohne Builder, Speichern, Entfernen und „Filter hinzufügen für“. Auf den Gruppen darunter bearbeitet er Filter wie alle anderen.
- **Verschieben** (Reiter Allgemein und Knotenmenü) und **Löschen** (Knotenmenü) sind auf seinen eigenen obersten Gruppen deaktiviert, mit dem Tooltip „Deine eigene oberste Gruppe: Ein übergeordneter Administrator verschiebt oder löscht sie.“; der Reiter **Rollen** ist dort schreibgeschützt, mit dem Hinweis warum. Umbenennen bleibt erlaubt. Ein Verschieben oder Löschen, das trotzdem beim Server ankommt (veraltete Seite), zeigt die übersetzte Ablehnung `identity.root_group_locked`.
- Der Reiter **Datenfilter** einer Rollenseite listet für ihn nichts: Die Listenanfrage antwortet `403`, und der Reiter sagt „Nur ein Administrator des ganzen Mandanten verwaltet diese Filter.“ statt eines Fehlers.

`DataFilterList` nimmt denselben Schalter auf eigenen Seiten:

```razor
<DataFilterList Layer="DataFilterLayer.Group" LayerKey="group.Id" ReadOnly="group.Locked" />
```

Beispiel: der Verwalter der Gruppe P (Untergruppe S, benachbarte oberste Gruppe Q außerhalb seiner Reichweite) mit `DataFilters.ManageGroups`:

```http
PUT /api/v1/data-filters  { "entitySet": "Shipments", "layer": "Group", "layerKey": "<S>", "query": "…" }  → 200
PUT /api/v1/data-filters  { "entitySet": "Shipments", "layer": "Group", "layerKey": "<P>", "query": "…" }  → 403 data_filters.forbidden (über ihm festgelegt)
PUT /api/v1/data-filters  { "entitySet": "Shipments", "layer": "Group", "layerKey": "<Q>", "query": "…" }  → 403 data_filters.forbidden (dieselbe Antwort wie für eine unbekannte Id)
GET /api/v1/data-filters?layer=Group                                                                       → die Filter von P und S
GET /api/v1/data-filters?layer=Role                                                                        → 403 (nur mit ManageRoles)
```

Ohne das Identity-Modul ist `IDataFilterReach` mandantenweit (das OData-Modul registriert die Vorgabe mit `TryAdd`, das Identity-Modul ersetzt sie). Eigene Anfragen, die Gruppenfilter schreiben, fragen sie genauso:

```csharp
var reach = await dataFilterReach.GetAsync(ct);                               // einmal pro Anfrage lesen
if (!reach.TenantWide && !reach.Editable.Contains(groupId)) return Forbidden; // keine Gruppe unter seinen obersten Gruppen
var visible = reach.TenantWide ? filters : filters.Where(f => reach.Readable.Contains(f.GroupId));
```

## Umstieg auf eine Version mit Datenfiltern

Die Tabellen `cw.DataFilters` und `cw.DataFilterLinks` müssen existieren, bevor die App startet: Jede Abfrage eines Entity-Sets schlägt sie bei kaltem Cache nach. Zuerst eine App-Migration anlegen und anwenden (`coworkee migrations add AddDataFilters`); die Bibliothek bringt keine eigene Migration mit. Das Abfragemodell ist nach `Coworkee.Contracts.Query` umgezogen; siehe das [Änderungsprotokoll](../reference/changelog.de.md).
