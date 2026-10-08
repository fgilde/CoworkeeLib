# Installation

## Voraussetzungen

- .NET SDK 10
- Docker (Postgres, Redis, Mailpit und Keycloak laufen als Container)
- Eine IDE mit Aspire-Unterstützung oder einfach die `dotnet`-CLI

## Mit dem Template starten

Das Template ist eine vollständige Anwendung: Katalog, Dokumente, Dashboard, Administration, geseedete Benutzer und Keycloak.

```bash
git clone https://github.com/fgilde/CleanArchitectureBaseBlazor myapp
cd myapp
dotnet run --project src/MyApp.AppHost
```

Die Konsole zeigt die Adresse des Aspire-Dashboards. Die App selbst läuft unter `https://localhost:7300`.

![Die Startseite des Templates](../assets/screenshots/home.png){ .shot }

## Anmelden

Der Migrations-Dienst spielt die Migrationen ein und legt die Demodaten an, bevor die anderen Dienste starten. Einen Setup-Assistenten gibt es im Template nicht.

| Benutzer | Rollen | Hinweis |
|---|---|---|
| `info@coworkee.de` | Administrator | existiert auch in Keycloak |
| `fgilde@gmail.com` | Administrator | |
| `john@coworkee.de` | keine | sieht nur, was Sie freigeben |

Die Passwörter stehen in `src/MyApp.Migrations/DemoSeed.cs`. Ändern Sie sie oder ersetzen Sie den Seed durch eigene Benutzer, bevor die App Ihren Rechner verlässt.

Die Anmeldeseite bietet zusätzlich **Sign in with Keycloak** an. Der Keycloak-Benutzer hat dieselbe Adresse wie der Administrator, deshalb verknüpft die erste Keycloak-Anmeldung beide Konten. Sein Passwort ist ein generierter Aspire-Parameter: im Dashboard die Ressource `myapp-keycloak-user-password`.

![Anmeldeseite mit Keycloak](../assets/screenshots/login.png){ .shot }

## Pakete für eine bestehende App

Alle Pakete beginnen mit `Coworkee.`. Nehmen Sie die, die Ihre Dienste brauchen; das Modulsystem zieht deren Abhängigkeiten nach:

```xml
<ItemGroup>
  <PackageReference Include="Coworkee.AspNetCore" />
  <PackageReference Include="Coworkee.Infrastructure" />
  <PackageReference Include="Coworkee.Identity" />
  <PackageReference Include="Coworkee.OData" />
</ItemGroup>
```

[Pakete und Namespaces](../reference/packages.md) listet, was in welchem Paket steckt.

!!! note "Gegen die Quellen bauen"
    Solange die Pakete nicht auf nuget.org liegen, packt `build/pack-local.ps1` im CoworkeeLib-Repository sie nach `artifacts/nuget`, und die `nuget.config` des Templates liest von dort.

## Weiter

[Aufbau der Solution](solution-structure.md) erklärt die Projekte des Templates, [Das erste Feature](first-feature.md) legt eine neue Entity von vorne bis hinten an.
