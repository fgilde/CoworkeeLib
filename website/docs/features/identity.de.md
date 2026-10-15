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

## Keycloak und andere Anbieter

Jeder OpenID-Connect-Anbieter kann Benutzer anmelden, jeder bekommt einen Abschnitt unter `Coworkee:Auth:External:Providers`. Mit einem bestehenden Konto verknüpft wird eine externe Anmeldung nur, wenn der Anbieter die E-Mail-Adresse als bestätigt meldet (oder mit `TrustEmail` konfiguriert ist). In der Entwicklung startet `options.UseKeycloak()` im Aspire-App-Host ein Keycloak mit einem Realm, der dieselben Benutzer und Passwörter hat wie die Anwendung.

Ob man sich per Passwort, über einen Anbieter oder auf beiden Wegen anmeldet, regelt die Einstellung `Auth.LoginMode`, auch im laufenden Betrieb. Die Selbstregistrierung (aus, solange `Account.AllowRegistration` nicht gesetzt ist) kann eine Freischaltung durch einen Administrator verlangen, dazu eine bestätigte Adresse, erlaubte Adressmuster und hochgeladene Dokumente. Auch die Passwortregeln sind Einstellungen, bis hin zu einem Verlauf von 24 Passwörtern, und jedes Passwortfeld nennt die gerade geltenden Regeln.

## Schlüssel und persönliche Daten

Skripte und KI-Clients nutzen persönliche API-Schlüssel: `cwk_` plus 64 Hex-Zeichen, einmal angezeigt, als SHA-256-Hash gespeichert, 7 bis 365 Tage gültig. Ein Schlüssel hat nie mehr Rechte als sein Benutzer im Moment; nimmst du dem Benutzer eine Rolle weg, ist sie beim nächsten Request auch beim Schlüssel weg.

Module, die Daten über eine Person speichern, implementieren `IPersonalDataContributor`. Über den Reiter *Datenschutz* der Kontoseite lädt man dann alles als `personal-data.json` herunter oder löscht das Konto.

**Stand:** Version 0.0.1.x. Die Admin-Seiten für Benutzer, Gruppen und Rollen liegen unter `/admin/...`; eine eigene Seite mit derselben Route bringt das Blazor-Routing durcheinander.

Referenz: [Anmeldung und Keycloak](../security/authentication.md), [Berechtigungen](../security/permissions.md), [Registrierung und Anmelderegeln](../security/registration.md), [Clients, Sitzungen und Passwortregeln](../security/clients.md), [Anmeldeseiten](../security/sign-in-pages.md), [API-Schlüssel](../security/api-keys.md), [Datenschutz](../security/privacy.md).
