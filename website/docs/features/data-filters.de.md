---
title: "Gestaffelte Datenfilter für ASP.NET-Core- und Blazor-Apps"
description: "Administratoren legen pro Entity-Set Filter auf vier Ebenen fest (System, Mandant, Gruppe, Rolle), die Benutzer nie sehen: Zusammenspiel, Details und verknüpfte Sets, Echtzeit und Admin-Oberfläche."
---

# Gestaffelte Datenfilter

Administratoren legen pro Entity-Set Filter fest, die immer gelten und die Benutzer nie sehen. Sie liegen auf vier Ebenen: **System**, **Mandant**, **Gruppe** und **Rolle**. Die eigene Filterung, Facetten und Zähler eines Benutzers arbeiten auf dem, was übrig bleibt. Nichts verrät dem Benutzer, dass Zeilen fehlen: Zähler, Facetten, Suche und Paging werden auf den gefilterten Zeilen berechnet.

Die Filter gelten überall, wo die Bibliothek entscheidet, welche Zeilen ein Benutzer sehen darf: OData-Listen und `$count`, `$expand`-Ziele, Client-Entities (lokale Kopien, Snapshots und Deltas), globale Suche, dynamische Berichte und Exporte. Ist für ein Set kein Filter eingerichtet, gibt es kein zusätzliches `Where`; das festzustellen kostet einen gecachten Nachschlag der Filter der Organisation (bei kaltem Cache und innerhalb einer Transaktion aus der Datenbank gelesen).

## Die vier Ebenen

| Ebene | Gilt für | Geschrieben mit |
|---|---|---|
| System | jede Organisation | `DataFilters.ManageSystem` (nur Systemorganisation) |
| Mandant | die eigene Organisation | `DataFilters.ManageTenant` |
| Gruppe | Mitglieder einer Gruppe (und ihrer Untergruppen) | `DataFilters.ManageGroups` |
| Rolle | Benutzer, die in einer Rolle arbeiten | `DataFilters.ManageRoles` |

`DataFilters.Bypass` („alle Zeilen sehen“) schaltet für den Inhaber die Ebenen Mandant, Gruppe und Rolle ab, und die Systemebene ebenfalls, wenn er zur Systemorganisation gehört. Die eingebaute Rolle Admin hat jede definierte Berechtigung, also haben die Administratoren jeder Organisation Bypass: Sie sehen die Daten ihrer Organisation ohne deren eigene Filter, die Systemfilter gelten für sie aber weiter; nur die Administratoren der Systemorganisation sehen die ungefilterten Daten. Filter werden als `QueryGroup` des [`QueryBuilder`](data.md) gespeichert und auf dem Server in `$filter` übersetzt; das Speichern prüft sie gegen das Modell des Sets und lehnt einen Filter ab, der eine Eigenschaft verwendet, die der speichernde Administrator nicht lesen darf (`HideUnless`).

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

Jeder Filter hat den Schalter **Gilt auch für Details**, standardmäßig an. An: Ein Link oder Schlüsselzugriff auf einen herausgefilterten Datensatz antwortet „nicht gefunden“. Aus: Die Liste blendet den Datensatz aus, der Schlüsselzugriff liefert ihn aber noch. Code, der einen einzelnen Datensatz über den Schlüssel liest, läuft in `DataFilterScope.ByKey()`; die OData-Pipeline macht das für Schlüsselanfragen, und was ein Datensatz expandiert, ist weiter eine Liste und bleibt gefiltert.

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

`total` zählt die Zeilen, die der Bearbeiter durch die eigenen Zeilenfilter der App ganz ohne Datenfilter sieht (für einen Systemadministrator jede Zeile); `passing` zählt davon die, die auch den Entwurf passieren. Gespeicherte Filter zählen nie mit, die Zahl zeigt also, was der Entwurf allein bewirkt. Die Anfrage braucht die Berechtigung ihrer Ebene, die Leseberechtigung auf das Set und dieselben Eigenschaftsprüfungen wie das Speichern (400 bei unbekannten oder geschützten Eigenschaften). Nach `CoworkeeODataOptions.DataFilterPreviewTimeout` (Standard 5 Sekunden) bricht das Zählen ab und antwortet `{ "passing": null, "total": null, "timedOut": true }`. Im Code schaltet `DataFilterScope.Off()` die Datenfilter für den aktuellen Ablauf aus; die eigenen Zeilenfilter der App gelten weiter.

## Übersicht

`GET /api/v1/data-filters/overview` sagt pro Entity-Set, wie viele Filter die Ebene des Administrators hält und ob das Set verknüpft ist:

```http
GET /api/v1/data-filters/overview

200 { "layer": "Tenant", "tenantName": "Acme", "sets": [ { "entitySet": "Orders", "layerFilters": 1, "linked": false } ] }
```

Die Ebene ist `System` im Systemmandanten (braucht `DataFilters.ManageSystem`), sonst `Tenant` (braucht `DataFilters.ManageTenant`). Verknüpfungen meldet nur der Systemmandant.

## Echtzeit

Jede Änderung lädt offene Tabellen neu und gleicht lokale Kopien ab; die Partition einer Client-Kopie enthält einen Fingerabdruck aller Filter der Organisation des Benutzers und der Systemfilter (Ids und Versionen, Verknüpfungen eingeschlossen), seiner aktiven Rollen, die einen Rollenfilter tragen, und, wenn es Gruppenfilter gibt, seiner aktiven Gruppen; ein geänderter Filter, ein Rollenwechsel oder eine andere Gruppe verwirft also die Kopie und holt die sichtbaren Zeilen neu. Ohne Filter bekommt die Partition nichts dazu. Änderungen senden Signale ohne Nutzdaten und ohne Akteur: Entities implementieren `IHasRealtimeSignals`, um die Topics zu nennen, der Ereignistyp ist `RealtimeEventTypes.Signal`. Ein Signal trägt eine leere Nutzlast (weder Entity-Name noch Id noch Filterinhalt) und keinen Akteur. Mandanten-, Gruppen- und Rollenfilter signalisieren auf `tenant:data-filters` (nur die eigene Organisation), Systemfilter und Verknüpfungen auf `global:data-filters`. „Signal“ ist ein reservierter Ereignisname: Eine App darf ihn nicht für eigene Ereignisse verwenden.

## Admin-Oberfläche

- **Datentypen** listet jedes Entity-Set mit seinen Client-Entity-Einstellungen sowie den System- und Mandantenfiltern und der Verknüpfung zu einem anderen Set. Die Systemeditoren und der Verknüpfungseditor erscheinen nur in der Systemorganisation.
- Der Reiter **Datenfilter** auf der Gruppenseite und der Rollenseite bearbeitet die Filter dieser Gruppe oder Rolle.
- Der Filtereditor ist der `QueryBuilder`: Regeln und Gruppen per Drag and Drop in andere Gruppen oder Positionen ziehen (oder über das Knotenmenü: hoch, runter, in eine Gruppe, aus einer Gruppe), eine Gruppe zwischen UND und ODER umschalten, angehakte Bedingungen in eine neue Gruppe einpacken, eine Gruppe auspacken. Auspacken gibt es nur, wenn die Bedeutung gleich bleibt: Die Logik der Gruppe entspricht der ihrer übergeordneten Gruppe, oder sie hat höchstens ein Kind und ist nicht negiert.

Lesen und Schreiben braucht die Berechtigung der Ebene; die Seite und der Navigationseintrag erscheinen für jeden mit einer der `DataFilters.Manage*`-Berechtigungen.

## Umstieg auf eine Version mit Datenfiltern

Die Tabellen `cw.DataFilters` und `cw.DataFilterLinks` müssen existieren, bevor die App startet: Jede Abfrage eines Entity-Sets schlägt sie bei kaltem Cache nach. Zuerst eine App-Migration anlegen und anwenden (`coworkee migrations add AddDataFilters`); die Bibliothek bringt keine eigene Migration mit. Das Abfragemodell ist nach `Coworkee.Contracts.Query` umgezogen; siehe das [Änderungsprotokoll](../reference/changelog.de.md).
