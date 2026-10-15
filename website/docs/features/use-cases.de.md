---
title: "Einsatzbeispiele für ein .NET-Anwendungsframework"
description: "Interne Admin-Werkzeuge, mandantenfähige B2B-SaaS, Kundenportale und Plattformen für digitale Assets auf Coworkee, dem Anwendungsframework für .NET und Blazor."
---

# Was Sie mit Coworkee bauen können

Coworkee ist an keine Branche gebunden. Es ist eine Basis für Anwendungen, in denen sich Menschen anmelden, mit Datensätzen arbeiten, sie mit Kollegen teilen und ein Administrator für Ordnung sorgt. Vier Arten von Apps zeigen, wo es passt, was sie vom Framework übernehmen und was Sie selbst schreiben.

## Internes Admin-Werkzeug oder Backoffice

**Die Lage.** Ein Team pflegt Produktdaten, Aufträge oder Verträge in Tabellen und auf einem Netzlaufwerk. Eine kleine Web-App soll das ablösen, mit Anmeldung über den Identity-Provider der Firma und einer brauchbaren Tabellenansicht.

**Was Coworkee abdeckt.** Anmeldung über [Keycloak oder jeden OpenID-Connect-Anbieter](../security/authentication.md), Rollen und [Berechtigungen pro Entity oder pro Datensatz](../security/permissions.md), eine [Datentabelle](../ui/data-table.md) mit Facetten, Spaltenfiltern, Gruppierung, gespeicherten Ansichten und Excel-Export, Bearbeitungsdialoge aus Ihren Request-Typen, ein [Audit-Log auf Feldebene](../modules/auditing.md) und [Datenbank-Backups](../modules/backups.md) aus der Administration.

**Was Sie schreiben.** Die Entities und ihre Regeln. Die Demo-App macht genau das für einen Produktkatalog und Aufträge: ein paar Entity-Klassen, die Requests für Anlegen und Ändern, eine OData-Registrierung und eine Seite pro Entity.

![Produkte nach Marke gruppiert, mit Facetten und Spaltenfiltern](../assets/screenshots/products-grouped-light.webp){ .shot loading=lazy }

## Mandantenfähige B2B-SaaS

**Die Lage.** Sie verkaufen eine Anwendung an viele Kundenfirmen. Jede Firma sieht nur ihre eigenen Daten, hat eigene Administratoren und zahlt für einen Tarif, der festlegt, welche Module sie bekommt.

**Was Coworkee abdeckt.** Organisationen als Mandanten, deren Daten EF Core per Abfragefilter trennt, [Features und Editionen](../modules/features.md), die Module pro Mandant freischalten und Grenzen wie „Berichte pro Monat“ tragen, [Einstellungen](../modules/settings.md), die ein Mandanten-Admin ändert, ohne Ihre anzufassen, Selbstregistrierung mit Freischaltung und Bestätigungsmail, [Themes](../modules/theming.md) pro Organisation und [Redis](../hosting/redis.md), sobald mehr als eine Instanz läuft.

**Was Sie schreiben.** Die Fachlichkeit Ihres Produkts, die Feature-Definitionen, die Ihre Preise abbilden, und die Texte fürs Onboarding.

```csharp
public void Define(FeatureDefinitionContext context) =>
    context.Group("Reports", "Reports")
        .Add("Reports.Enabled", "Reports")
        .Add("Reports.MaxPerMonth", "Reports per month", FeatureType.Int, "10");
```

## Kunden- oder Partnerportal

**Die Lage.** Kunden sollen ihre Aufträge nachsehen, Dokumente herunterladen und Fragen stellen, ohne anzurufen. Das Portal soll nach Ihrer Marke aussehen, ihre Sprache sprechen und auf dem Telefon funktionieren.

**Was Coworkee abdeckt.** Ein Registrierungsassistent mit optionaler Adresse, Rollenwahl und Dokument-Upload, [drei Anmelde-Designs](../security/sign-in-pages.md) in Ihren Farben, [öffentliche Dateien](../modules/files.md) mit anonymen Links, [Benachrichtigungen und Chat](../modules/realtime.md), [Mail-Vorlagen und Kampagnen](../modules/mailing.md) und [23 mitgelieferte Sprachen](../modules/localization.md) mit maschineller Übersetzung für Ihre eigenen Texte. Berechtigungen auf einzelne Ressourcen sorgen dafür, dass ein Kunde seine Aufträge sieht und sonst nichts.

**Was Sie schreiben.** Die Sichten auf Ihre Daten, die Kunden sehen dürfen, und die Regeln, wer was sieht.

![Die Anmeldeseite auf dem Telefon](../assets/screenshots/signin-phone-light.webp){ .shot loading=lazy }

## Plattform für digitale Assets und Dokumente

**Die Lage.** Eine Firma verwaltet Bilder, Videos, Dokumente und deren Metadaten, mit Versionen, Suche und fein abgestufter Freigabe.

**Was Coworkee abdeckt.** Darauf baut [Sharemee](https://github.com/fgilde/sharemee) auf: Digital Asset Management, Dokumentenverwaltung, Wissensdatenbank und Intranet auf einer .NET-10-Plattform. Identity, Berechtigungen pro Element, Audit, Realtime, Einstellungen, die Admin-Oberfläche, die REST-API und den MCP-Endpunkt nimmt es von Coworkee und ergänzt eigene Module für Assets, Metadaten, Verarbeitung, Suche und Arbeitsbereiche.

**Was Sie schreiben.** Die aufwendigen fachlichen Teile, und genau darum geht es: Sharemees eigener Code handelt von Assets, nicht von Benutzerverwaltung.

## Apps mit KI-Assistent

Jede der Apps oben kann den [KI-Assistenten](ai.md) einschalten. Er arbeitet über dieselben Requests wie die Oberfläche, mit den Rechten des angemeldeten Benutzers, und fragt nach, bevor er Daten ändert. Sie wählen einen von 14 Anbietern, von Anthropic und OpenAI bis zu einem lokalen Ollama-Modell, und die App stellt zusätzlich einen MCP-Endpunkt bereit, über den Werkzeuge wie Claude oder Copilot mit persönlichen API-Schlüsseln auf Ihre Daten zugreifen.

## Wann Coworkee nicht passt

- Eine öffentliche Website oder ein Blog ohne angemeldete Benutzer. Ein statischer Seitengenerator ist einfacher.
- Eine einzelne kleine API ohne Oberfläche, Admins oder Mandanten. Schlichte Minimal-APIs in ASP.NET Core schaffen das mit weniger.
- Ein Team, das auf .NET 8 oder einer älteren Laufzeit bleiben muss. Coworkee setzt .NET 10 voraus.

## Weiterlesen

- [Installation](../getting-started/installation.md): die Demo-App in wenigen Minuten starten.
- [Kommandozeile](../getting-started/cli.md): `coworkee new` mit den Optionen für jeden der Fälle oben.
- [Warum Coworkee](why-coworkee.md): was ein Framework gegenüber einem selbst gebauten Projekt spart.
