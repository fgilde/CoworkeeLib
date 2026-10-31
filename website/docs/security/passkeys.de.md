# Passkeys

Benutzer melden sich am Auth-Server mit einem Passkey (WebAuthn) statt mit dem Passwort an: per Fingerabdruck, Gesicht oder Bildschirmsperre eines Geräts, mit einem Telefon in der Nähe oder einem Sicherheitsschlüssel. Ein Passkey gehört zu genau einem Origin, eine nachgemachte Seite kann ihn also nicht verwenden. Die Zeremonien erledigt ASP.NET Core Identity; Coworkee bringt die Seiten, die Speicherung und die Verwaltung dazu.

## Für Benutzer

- **Hinzufügen**: *Kontosicherheit › Passkeys* (`/Account/Manage/Passkeys`, in der App *Profil › Sicherheit › Passkeys*) fügt einem bestehenden Konto einen Passkey hinzu. Zuerst wird der Name abgefragt; vorbelegt sind Browser und Plattform (zum Beispiel `Chrome · Windows`). Ein Benutzer hat höchstens 20 Passkeys.
- **Liste**: Name, Tag des Hinzufügens und ob der Passkey gesichert ist (von der Plattform synchronisiert, z. B. iCloud-Schlüsselbund oder Google Passwortmanager) oder nur auf einem Gerät liegt. Passkeys lassen sich umbenennen und entfernen.
- **Der letzte bleibt**: Wer weder Passwort noch externe Anmeldung hat, kann den letzten Passkey nicht entfernen; er ist dann der einzige Zugang.
- **Anmelden**: Die Anmeldeseite hat unter dem Passwortformular *Mit Passkey anmelden*, und das Adressfeld bietet die Passkeys des Browsers im Autofill an (Conditional UI, `autocomplete="username webauthn"`). Browser ohne WebAuthn zeigen beides nicht.

Eine Anmeldung per Passkey gilt als starke Anmeldung: Die Bestätigung in zwei Schritten folgt nicht, wie in ASP.NET Core Identity. Deshalb muss das Gerät den Benutzer prüfen (Fingerabdruck, Gesicht, PIN): Die Anmeldung lehnt eine Assertion ohne Benutzerverifizierung ab, unabhängig von der Konfiguration. Alles andere wird wie beim Passwort geprüft: Inaktive Konten, `Coworkee:Auth:Login:AllowedEmails`, die bestätigte Adresse und die Sperre geben dieselben Antworten, und die Anmeldung vermerkt den letzten Login.

Eine Registrierung ohne Passwort gibt es nicht; ein Passkey kommt immer zu einem bestehenden Konto hinzu.

Solange jemand als der Benutzer angemeldet ist ([Anmelden als](../features/identity.md#als-anderer-benutzer-anmelden)), antwortet die Passkey-Seite wie jede Kontoseite mit `403`: Ein Impersonator fügt dem Konto nie einen Zugang hinzu.

## Für Administratoren

Der Anmeldebereich eines Benutzers (**Administration › Benutzer**, Benutzerseite) zeigt *Passkeys: n* und *Alle entfernen*, zum Beispiel nach einem verlorenen Gerät. Das braucht `Identity.Users.Manage`, erneuert den Security-Stamp und meldet den Benutzer damit überall ab. Anders als die eigene Seite des Benutzers entfernt es auch den letzten Passkey eines Kontos ohne Passwort; einem solchen Benutzer schicken Sie dann einen Passwort-Reset oder eine Einladung. Dasselbe über die API:

```http
POST /api/v1/identity/users/{id}/passkeys/reset
```

`UserDetailDto.PasskeyCount` und `ProfileDto.PasskeyCount` enthalten die Anzahl. Der Export der persönlichen Daten listet jeden Passkey mit Name, Datum und Sicherungsstatus, nie mit dem öffentlichen Schlüssel. Wird ein Benutzer gelöscht, gehen seine Passkeys per Cascade mit.

## Abschalten

Die globale Einstellung `Account.Passkeys.Enabled` (Gruppe *Anmeldesicherheit*, Standard an) blendet Button, Autofill, Kontoseite und den Eintrag in der App aus; die Handler antworten mit `404`. Gespeicherte Passkeys bleiben erhalten und funktionieren wieder, sobald die Einstellung wieder an ist.

Im Login-Modus *External* (nur der externe Provider, z. B. Keycloak, meldet an) sind Passkeys ebenfalls aus: Der Provider hat eigene.

## Konfiguration

`Coworkee:Auth:Passkeys` bindet die `IdentityPasskeyOptions` von ASP.NET Core Identity:

```json
"Coworkee": {
  "Auth": {
    "Passkeys": {
      "ServerDomain": "auth.example.com",
      "AuthenticatorTimeout": "00:05:00"
    }
  }
}
```

| Option | Bedeutung |
|---|---|
| `ServerDomain` | die Relying-Party-ID, also der Host, zu dem die Passkeys gehören. Ohne Angabe gilt der Host der Anfrage: `localhost` in der Entwicklung, `auth.example.com` in Produktion. Setzen, wenn der Auth-Server hinter einem Proxy ohne Forwarded Headers läuft und Anfragen deshalb einen internen Host tragen. Passkeys einer Domain funktionieren auf keiner anderen; beim Umzug des Servers also beibehalten. |
| `AuthenticatorTimeout` | wie lange der Browser auf den Authenticator wartet (Standard 5 Minuten) |
| `UserVerificationRequirement` | `required` (Standard). `preferred` oder `discouraged` ändern nur, was der Browser anfragt: Die Anmeldung lehnt Passkeys, die den Benutzer nicht geprüft haben, trotzdem ab |
| `ResidentKeyRequirement` | `preferred` (Standard), `required` oder `discouraged` |
| `AttestationConveyancePreference`, `AuthenticatorAttachment` | gehen an den Browser; eine Herstellerliste wird nicht geprüft |

Der Browser prüft den Origin der Seite gegen die Relying Party, der Server vergleicht ihn mit dem `Origin`-Header der Anfrage. Der Auth-Server hat immer einen eigenen Origin (Entwicklung `https://localhost:7302`).

## Skript und Content Security Policy

`passkey.js` (eingebettet, `/auth-assets/passkey.js`) übernimmt die Browserseite: Es holt die Optionen von der Seite, ruft `navigator.credentials.create` oder `get` auf und schickt das Ergebnis als Formularfeld. Wo der Browser `PublicKeyCredential.parseCreationOptionsFromJSON` und `toJSON` kennt, nutzt es sie, sonst kodiert es base64url selbst. Die Policy des Auth-Servers bleibt `script-src 'self'`.

## Upgrade

Passkeys liegen in der neuen Tabelle `cw.UserPasskeys` (Schlüssel `CredentialId`, `Data` als JSON, Cascade von `cw.Users`); bestehende Apps fügen eine Migration hinzu. Siehe [Änderungsprotokoll](../reference/changelog.md).
