# Registrierung und Anmelderegeln

Neue Benutzer registrieren sich selbst auf dem Auth-Server mit einem Assistenten. Er wird wie die übrigen Kontoseiten auf dem Server gerendert und braucht kein Skript:

1. **Konto**: E-Mail und Passwort (geprüft gegen die Passwortregeln).
2. **Persönliche Daten**: Vor- und Nachname, Telefon und, wenn verlangt, die Postanschrift.
3. **Rolle**: nur wenn Rollen zur Registrierung angeboten werden; der Benutzer darf mehrere wählen.
4. **Dokumente** oder **Zusammenfassung**: alle Eingaben im Überblick, dazu ein Upload je Dokument-Slot, wenn Dokumente verlangt sind. Browser behalten eine gewählte Datei nicht über mehrere Seiten, darum kommen die Dokumente mit dem letzten Absenden.

Jeder Schritt prüft seine Felder, bevor der nächste öffnet; *Zurück* behält die Eingaben. Zwischen den Schritten reisen die Eingaben verschlüsselt (ASP.NET Core Data Protection, zwei Stunden) in einem versteckten Feld, der Server hält keine Sitzung. Das letzte Absenden prüft alle Schritte noch einmal.

## Einstellungen, die Admins zur Laufzeit ändern

Unter **Administration > Einstellungen > Accounts**:

| Einstellung | Standard | Bedeutung |
|---|---|---|
| `Account.AllowRegistration` | `false` | zeigt den Assistenten und den Link *Konto erstellen*; externe Anmeldungen dürfen Konten anlegen |
| `Account.RegistrationRequiresActivation` | `true` | neue Konten bleiben inaktiv, bis ein Administrator sie freischaltet; Administratoren werden benachrichtigt (wenn `Coworkee.Notifications` im Auth-Host läuft) |
| `Account.RegistrationRequiresEmailConfirmation` | `true` | der Benutzer bestätigt die Adresse mit dem Link aus der Mail vor der ersten Anmeldung |

Standardwerte für eine neue Installation kommen aus der Konfiguration, zum Beispiel `Coworkee:Settings:Defaults:Account.AllowRegistration = true`.

## Konfiguration

```json
"Coworkee": {
  "Registration": {
    "RequireAddress": true,
    "AllowedEmails": [ "*@example.com" ],
    "Password": { "RequiredLength": 8, "RequireUppercase": true, "RequireLowercase": true, "RequireDigit": true, "RequireNonAlphanumeric": false },
    "RequireDocuments": true,
    "Documents": [
      { "Name": "Passport", "Description": "A scan of your passport", "ContentTypes": [ "image/*", "application/pdf" ], "MaxSize": 4000000 },
      { "Name": "Certificate", "Required": false, "ContentTypes": [ "application/pdf" ] }
    ]
  }
}
```

| Einstellung | Bedeutung |
|---|---|
| `RequireAddress` | Straße, PLZ, Ort und Land sind in Schritt 2 Pflicht |
| `AllowedEmails` | Muster mit `*`, zu denen eine Adresse passen muss, um sich zu registrieren; leer erlaubt alle |
| `Password` | die `PasswordOptions` von ASP.NET Core Identity; sie gelten für jedes Passwort, auch für die von Admins gesetzten |
| `RequireDocuments` | zeigt die Dokument-Uploads |
| `Documents` | ein Slot je Dokument: `Name`, `Description`, `Required` (Standard `true`), `ContentTypes` (Platzhalter wie `image/*` oder `application/vnd.openxmlformats-officedocument.*`, leer nimmt alle), `MaxSize` in Bytes (leer oder 0 ohne Grenze) |

## Rollen in der Registrierung

Eine Rolle mit **In der Registrierung angeboten** (auf der Rollenseite, `RoleRequest.SelectableForRegistration`, `SeedRole(..., SelectableForRegistration: true)`) erscheint in Schritt 3. Wer eine nicht angebotene Rolle schickt, bleibt mit einem Fehler im Schritt stehen. Systemrollen werden nie angeboten.

Das Kennzeichen ist eine Spalte von `cw.Roles`. Apps mit eigenen Migrationen legen nach dem Update eine an: `dotnet ef migrations add RoleSelectableForRegistration`.

## Wohin die Dokumente gehen

Der Auth-Server gibt jede Datei an einen `IRegistrationDocumentStore` (in `Coworkee.Application.Registration`). Er läuft als der neue Benutzer in derselben Arbeitseinheit, die das Konto anlegt:

```csharp
public interface IRegistrationDocumentStore
{
    Task SaveAsync(RegistrationDocument document, CancellationToken cancellationToken);
}
```

- **`Coworkee.Files`** bringt einen Standard mit: die Dateien landen in *Registrations/{E-Mail}*. Der Ordner hat genau eine Freigabe, für den neuen Benutzer über die Rolle *Registration documents* (darf Dateien sehen); nur der Benutzer und Inhaber einer globalen Datei-Berechtigung, meist Administratoren, sehen ihn.
- **Ein eigener Store** gewinnt gegen den Standard, wenn Ihr Modul ihn später registriert (`services.AddScoped<IRegistrationDocumentStore, MyStore>()`). Eine App mit eigenem Dokumentenmodul legt sie dort ab, zum Beispiel als Dokumente vom Typ *Registration*, im Besitz des Benutzers und nicht öffentlich.

Verlangte Dokumente ohne jeden Store halten den Auth-Server mit einer klaren Fehlermeldung an.

## Anmelderegeln

```json
"Coworkee": {
  "Auth": {
    "External": { "Mode": "Both" },
    "Login": { "AllowUserName": true, "AllowedEmails": [ "*@example.com" ] }
  }
}
```

| Einstellung | Bedeutung |
|---|---|
| `External:Mode` | `Internal` zeigt nur das Passwortformular, `External` nur die Anbieter-Buttons (und blendet die Registrierung aus), `Both` beides |
| `Login:AllowUserName` | das Passwortformular nimmt neben der E-Mail auch den Benutzernamen |
| `Login:AllowedEmails` | Muster, zu denen eine Adresse passen muss, um sich anzumelden, mit Passwort oder extern; leer erlaubt alle |

Konten mit unbestätigter E-Mail können sich nicht mit Passwort anmelden.

## Externe Anmeldung und bestehende Konten

Bei einer externen Anmeldung sucht der Auth-Server den Benutzer über den verknüpften Login, dann über die E-Mail, dann legt er einen an:

- **Verknüpfen über die E-Mail** braucht eine bestätigte Adresse: der Anbieter schickt `email_verified: true`, oder er ist mit `TrustEmail` konfiguriert (`Coworkee:Auth:External:Providers:{name}:TrustEmail`). Das Keycloak des App-Hosts gilt als vertrauenswürdig (`CoworkeeKeycloakOptions.TrustEmail`, Standard `true`), sein Realm gehört zur App.
- Ein lokales Konto, dessen eigene Adresse **nicht bestätigt** ist, wird nie verknüpft: jemand könnte eine fremde Adresse registriert haben. Der Inhaber bestätigt sie zuerst.
- **Neue Konten** brauchen `AutoProvision` und `Account.AllowRegistration`, und die Adresse muss zu `Registration:AllowedEmails` passen. Sie folgen `Account.RegistrationRequiresActivation`: ein inaktives Konto wartet auf einen Administrator, der benachrichtigt wird.

## Keine anonymen Seiten

`CoworkeeClientOptions.AllowAnonymous = false` schickt jeden nicht angemeldeten Besucher von jeder Seite des Layouts direkt zur Anmeldung, mit der Seite als Rücksprungadresse. Der Einrichtungsassistent bleibt erreichbar. BFF und API behalten ihre eigenen Regeln.

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options => options.AllowAnonymous = false);
```

## Sprachen

Die Kontoseiten folgen der Browsersprache (`Accept-Language`). Der englische Text ist der Schlüssel; `Texts/de.json` in `Coworkee.AuthServer` enthält Deutsch.
