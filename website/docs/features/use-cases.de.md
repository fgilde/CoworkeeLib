---
title: "Einsatzbeispiele für ein .NET-Anwendungsframework"
description: "Backoffice, mandantenfähige B2B-SaaS, Kundenportale und eine Plattform für digitale Assets auf Coworkee, dem Anwendungsframework für .NET, Blazor und Aspire."
---

# Einsatzbeispiele

## Sharemee, die Asset-Plattform

Sharemee ist nach der Demo die zweite App, die ich auf Coworkee gebaut habe. Eine Plattform für digitale Assets und Dokumente, mit Wissensdatenbank und Intranet obendrauf, und noch ziemlich am Anfang.

Aus der Bibliothek nimmt sie Identity, Berechtigungen pro Element, den Audit-Trail, Realtime, Einstellungen, die Admin-Seiten, die REST-API und den MCP-Endpunkt. Sharemees eigene Projekte heißen Assets, Metadata, Processing, Search und Workspaces, und ungefähr das steht auch drin. Wenn ich etwas an der Anmeldung repariere, bekommt Sharemee das mit dem nächsten Paket-Update.

## Ein Backoffice für Aufträge und Produkte

Genau das ist die Demo-App: ein Produktkatalog, Dokumente und 100.000 Aufträge, mit einer [Datentabelle](../ui/data-table.md) pro Entity. Facetten, Spaltenfilter, Gruppierung, gespeicherte Ansichten, Excel-Export. Die Bearbeitungsdialoge entstehen aus den Request-Typen, jede Feldänderung landet im [Audit-Log](../modules/auditing.md), und ein Admin zieht aus der Administration ein [Datenbank-Backup](../modules/backups.md).

![Produkte nach Marke gruppiert, mit Facetten und Spaltenfiltern](../assets/screenshots/products-grouped-light.webp){ .shot loading=lazy }

Der eigene Code der Demo: ein paar Entity-Klassen, die Requests zum Anlegen und Ändern, eine OData-Registrierung pro Set und eine Seite pro Entity. Angemeldet wird über das Keycloak der Firma oder jeden anderen [OpenID-Connect-Anbieter](../security/authentication.md).

## Mandantenfähige B2B-SaaS

Eine App, viele Kundenfirmen. Jede Organisation sieht nur ihre eigenen Zeilen (EF Core hängt den Mandantenfilter an jede Abfrage), hat eigene Admins und bekommt die Module, die ihr Tarif enthält. Tarife sind [Editionen](../modules/features.md), und ein Feature kann neben An/Aus auch eine Grenze tragen:

```csharp
public void Define(FeatureDefinitionContext context) =>
    context.Group("Reports", "Reports")
        .Add("Reports.Enabled", "Reports")
        .Add("Reports.MaxPerMonth", "Reports per month", FeatureType.Int, "10");
```

Mandanten-Admins ändern ihre [Einstellungen](../modules/settings.md), ohne deine anzufassen. Läuft mehr als eine Instanz, hält [Redis](../hosting/redis.md) Cache und Realtime synchron.

## Ein Kundenportal

Kunden registrieren sich über einen Assistenten (Adresse, Rolle und Dokument-Upload sind optionale Schritte), melden sich auf einer Seite in deinen Farben an und sehen ihre eigenen Aufträge, weil sich [Berechtigungen](../security/permissions.md) auf einen einzelnen Datensatz vergeben lassen. [Öffentliche Dateien](../modules/files.md) bekommen anonyme Links für alles, was jeder herunterladen darf.

![Die Anmeldeseite auf dem Telefon](../assets/screenshots/signin-phone-light.webp){ .shot loading=lazy }

Die meisten Portale brauchen mehr als eine Sprache. Die Pakete bringen 23 mit, Mails und Anmeldeseiten eingeschlossen, und deine eigenen Texte lassen sich maschinell übersetzen (am besten danach von jemandem gegenlesen, der die Sprache spricht).

## Alles davon, mit Assistent

Der [KI-Assistent](ai.md) arbeitet über dieselben Requests wie die Oberfläche, mit den Rechten des angemeldeten Benutzers. Jede Änderung an Daten wartet, bis der Benutzer auf *Ausführen* klickt.

## Wofür es nicht taugt

- Eine öffentliche Website oder ein Blog. Wenn sich niemand anmeldet, liegt das meiste brach.
- Eine kleine API ohne Oberfläche und ohne Admins. Mit Minimal-APIs bist du schneller fertig.
- Alles, was auf .NET 8 bleiben muss. Coworkee braucht .NET 10.

Zum Ausprobieren: [CLI installieren](../getting-started/installation.md) und `coworkee new` aufrufen.
