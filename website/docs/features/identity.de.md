---
title: "Identität und Berechtigungen für ASP.NET Core"
description: "Benutzer, Rollen, Gruppen und Berechtigungen bis auf einzelne Ressourcen, ein OpenIddict-Auth-Server mit BFF, Keycloak oder jeder OIDC-Anbieter, API-Schlüssel."
---

# Identität, Anmeldung und Berechtigungen für .NET-Geschäftsanwendungen

Jede Geschäftsanwendung braucht dieselbe Sicherheitsgrundlage, bevor sie irgendetwas Nützliches tut: Konten, Rollen, eine Anmeldung, die für den Browser und für Skripte funktioniert, Regeln dafür, wer sich registrieren darf, eine Möglichkeit, jemanden sofort auszusperren, und eine Antwort, wenn ein Benutzer seine Daten anfordert. Das selbst zu schreiben dauert Wochen, und die Fehler zeigen sich im Audit.

Coworkee liefert das als Pakete. Ein OpenID-Connect-Server auf Basis von OpenIddict meldet Benutzer an, ein Backend for Frontend hält die Tokens vom Browser fern, und ein Berechtigungssystem prüft jeden Request auf dem Server. Administratoren verwalten Benutzer, Rollen, Gruppen, Clients und Passwortregeln in der Anwendung, ohne Deployment.

![Rolleneditor mit der Berechtigungsmatrix, nach Modulen gruppiert](../assets/screenshots/role-permissions.png){ .shot loading=lazy }

## Benutzer, Rollen und Gruppen

Administratoren bearbeiten alles zu einem Benutzer auf einer Seite: Namen, Kontaktdaten, Sprache, Aktiv-Status, E-Mail-Adresse (mit oder ohne Bestätigungsmail), Passwort oder Link zum Zurücksetzen, eine Einladung für Benutzer ohne Passwort, Sperre, Zurücksetzen der Zwei-Schritt-Verifizierung, externe Anmeldungen, Rollen, Gruppen und das Bild. Neue Benutzer entstehen in einem Dialog und werden per Mail eingeladen oder bekommen ein Startpasswort.

Berechtigungen gehen an Rollen, an Gruppen oder an einzelne Benutzer. Rollen lassen sich im Code anlegen, sodass eine frische Installation mit den Rollen startet, die Ihre Anwendung braucht:

```csharp
seed.Roles.Add(new SeedRole("Product Manager", "Maintains products",
    [CatalogPermissions.Products.View, CatalogPermissions.Products.Create, CatalogPermissions.Products.Edit]));
```

## Berechtigungen bis auf eine einzelne Ressource

Jedes Modul definiert seine Berechtigungen mit einem `IPermissionDefinitionContributor`. Aus den Definitionen entsteht die Berechtigungsmatrix im Rolleneditor: gruppiert, durchsuchbar, mit einem Schalter pro Gruppe. Auf dem Server erklärt ein Request, was er braucht, und der Dispatcher prüft das, bevor der Handler läuft:

```csharp
[RequiresPermission(CatalogPermissions.Products.Delete)]
public sealed record DeleteProductsCommand(IReadOnlyList<Guid> Ids) : ICommand<Result>;
```

Innerhalb eines Handlers beantwortet `IPermissionChecker.IsGrantedAsync` dieselbe Frage, eine Überladung nimmt Ressourcentyp und Id für Freigaben auf einen Ordner oder einen Datensatz. Freigaben vererben sich entlang einer Hierarchie, die Sie mit `IResourceHierarchy` beschreiben; `IResourceRestriction` nimmt bestimmten Rollen den Zugriff wieder weg. In Blazor blenden `PermissionGate`, Navigationseinträge und Tabellenschaltflächen aus, was der Benutzer nicht nutzen darf. Die Oberfläche blendet nur aus; entscheiden tut der Server.

## OpenIddict-Auth-Server und BFF

Drei Hosts teilen sich die Arbeit. Der Auth-Server (`Coworkee.AuthServer`) übernimmt Anmeldung, Registrierung, Passwort-Zurücksetzen, Zwei-Schritt-Verifizierung und die Kontoseiten. Der Web-Host (`Coworkee.Bff`) führt den OpenID-Connect-Ablauf mit PKCE aus, hält die Tokens in einem serverseitigen Sitzungsspeicher (Redis, wenn konfiguriert) und gibt dem Browser nur ein Cookie mit einem Sitzungsschlüssel. Die API prüft Bearer-Tokens mit `AddCoworkeeApiAuthentication`. Der Browser sieht nie ein Access Token.

Administratoren der Systemorganisation verwalten weitere OpenID-Connect-Clients und Scopes unter *Administration › Anwendungen* und *Scopes*: öffentlich oder vertraulich, implizite oder ausdrückliche Zustimmung, Refresh Tokens und Service-Clients, die APIs per `client_credentials` aufrufen.

## Keycloak oder jeder OpenID-Connect-Anbieter

Externe Anmeldung ist Konfiguration, ein Abschnitt pro Anbieter:

```json
"Coworkee": {
  "Auth": {
    "External": {
      "Mode": "Both",
      "AutoProvision": true,
      "Providers": {
        "keycloak": { "DisplayName": "Keycloak", "Authority": "https://keycloak.example.com/realms/myapp", "ClientId": "myapp-auth", "ClientSecret": "..." }
      }
    }
  }
}
```

Eine externe Anmeldung wird nur dann mit einem bestehenden Konto verknüpft, wenn der Anbieter die E-Mail-Adresse als bestätigt meldet oder mit `TrustEmail` konfiguriert ist. In der Entwicklung startet `options.UseKeycloak()` im Aspire-App-Host Keycloak mit einem Realm für die Anwendung, denselben Benutzern und Passwörtern wie in der Anwendung, und bringt den Realm bei jedem Start auf den aktuellen Stand. Mit der Einstellung `Auth.LoginMode` wechseln Administratoren zur Laufzeit zwischen Passwort, externen Anbietern oder beidem.

![Keycloak-Anmeldeseite für den Realm der Anwendung](../assets/screenshots/keycloak.png){ .shot loading=lazy }

Die Kontoseiten gibt es in drei Layouts (Card, Split, Slide), und sie folgen dem Theme der Anwendung; [Anmeldeseiten und Designs](../security/sign-in-pages.md) beschreibt sie.

## Registrierungs- und Passwortregeln als Einstellungen

Die Selbstregistrierung ist ein serverseitig gerenderter Assistent: Konto, persönliche Daten, optional eine Rollenwahl und optional Dokument-Uploads. Administratoren entscheiden zur Laufzeit, ob die Registrierung offen ist (`Account.AllowRegistration`), ob neue Konten auf Freischaltung warten und ob die E-Mail-Adresse zuerst bestätigt werden muss. Über die Konfiguration kommen erlaubte Adressmuster wie `*@example.com`, eine Pflicht-Postanschrift und Dokumentfächer mit Dateitypen und Größenlimits dazu.

Auch die Passwortregeln sind Einstellungen: Länge, verschiedene Zeichen, Ziffern, Groß- und Kleinbuchstaben, Sonderzeichen, Sperre nach Fehlversuchen, Ablauf in Tagen und ein Verlauf von bis zu 24 Passwörtern. `SettingsPasswordValidator` setzt sie bei Registrierung, Zurücksetzen, Ändern, bei von Administratoren gesetzten Passwörtern und im Einrichtungsassistenten durch, und jedes Passwortfeld zeigt die geltenden Regeln.

## Sitzungen, die Sie sofort beenden

*Überall abmelden* und *Sperren* auf der Seite eines Benutzers wirken sofort. Access Tokens tragen einen Hash des Security Stamps; die API vergleicht ihn bei jedem Request, Redis Pub/Sub informiert die anderen API-Instanzen, offene Realtime-Verbindungen bekommen `SessionRevoked`, und das BFF verwirft die Cookie-Sitzung. Wer sein eigenes Passwort ändert, beendet alle anderen Sitzungen und behält die aktuelle.

## Persönliche API-Schlüssel

Benutzer mit `ApiKeys.Create` legen unter *Konto > API-Schlüssel* Schlüssel für Skripte und KI-Clients an, mit einer Laufzeit von 7 bis 365 Tagen und Berechtigungen aus ihren eigenen. Der Schlüssel wird einmal angezeigt (`cwk_` plus 64 Hex-Zeichen); der Server speichert nur seinen SHA-256-Hash. Bei jedem Request werden die Berechtigungen des Schlüssels mit den aktuellen Berechtigungen seines Benutzers geschnitten: Verliert der Benutzer eine Rolle, verliert sie auch der Schlüssel.

```bash
curl -H "X-Api-Key: cwk_..." https://api.myapp.example/odata/Products?$top=5
```

## Export personenbezogener Daten und Kontolöschung

Jedes Modul, das Daten über einen Benutzer speichert, implementiert `IPersonalDataContributor`: einen benannten JSON-Abschnitt für den Export und einen Löschschritt. Die Kontoseite hat einen Reiter *Datenschutz*, um `personal-data.json` herunterzuladen oder das Konto zu löschen; Administratoren löschen Benutzer auf der Benutzerseite. Der letzte aktive Administrator lässt sich nicht löschen.

## Weiterlesen

- [Anmeldung und Keycloak](../security/authentication.md): die drei Hosts, Clients, externe Anbieter, Einrichtungsassistent oder Seed
- [Berechtigungen](../security/permissions.md): Berechtigungen definieren und auf Server und in Blazor prüfen
- [Registrierung und Anmelderegeln](../security/registration.md): Assistent, Freischaltung, Dokumente und Anmeldemethoden
- [Clients, Sitzungen und Passwortregeln](../security/clients.md): OIDC-Clients, Service-Clients, überall abmelden
- [Anmeldeseiten und Designs](../security/sign-in-pages.md): Layouts, Medien und eigene Designs
- [API-Schlüssel](../security/api-keys.md): persönliche Schlüssel anlegen, nutzen und prüfen
- [Datenschutz und personenbezogene Daten](../security/privacy.md): eingebaute Abschnitte, Export- und Lösch-Endpunkte
- [Aspire-App-Host: Keycloak](../hosting/aspire.md#keycloak): der lokale Keycloak-Realm
