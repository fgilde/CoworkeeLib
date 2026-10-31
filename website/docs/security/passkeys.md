# Passkeys

Users sign in to the auth server with a passkey (WebAuthn) instead of the password: the fingerprint, the face or the screen lock of a device, a phone nearby or a security key. A passkey belongs to one origin, so a look-alike page cannot use it. ASP.NET Core Identity does the ceremonies; Coworkee adds the pages, the storage and the administration.

## For users

- **Adding**: *Account security › Passkeys* (`/Account/Manage/Passkeys`, in the app *My profile › Security › Passkeys*) adds a passkey to an existing account. The name is asked first; it starts with the browser and the platform (for example `Chrome · Windows`). A user has at most 20 passkeys.
- **List**: name, the day it was added and whether it is backed up (synced by the platform, e.g. iCloud Keychain or Google Password Manager) or lives on one device only. Passkeys can be renamed and removed.
- **The last one stays**: a user without a password and without an external sign-in cannot remove the last passkey; it is the only way in.
- **Signing in**: the sign-in page has *Sign in with a passkey* below the password form, and the address field offers the passkeys of the browser in its autofill (conditional UI, `autocomplete="username webauthn"`). Browsers without WebAuthn show neither.

A passkey sign-in counts as a strong sign-in: two-step verification does not follow, as in ASP.NET Core Identity. That is why the device has to verify the user (fingerprint, face, PIN): the sign-in refuses an assertion without user verification, whatever the configuration says. Everything else is checked as for the password: inactive accounts, `Coworkee:Auth:Login:AllowedEmails`, the confirmed address and the lockout give the same answers, and the sign-in records the last login.

Registering without a password is not possible; a passkey always joins an existing account.

While someone is signed in as the user ([Log in as](../features/identity.md#log-in-as-another-user)), the passkey page answers `403` like every account page: an impersonator never adds a way into the account.

## For administrators

The sign-in panel of a user (**Administration › Users**, user page) shows *Passkeys: n* and *Remove all*, for example after a lost device. It needs `Identity.Users.Manage`, renews the security stamp and so signs the user out everywhere. Unlike the user's own page it also removes the last passkey of an account without password; send such a user a password reset or an invitation. The same through the API:

```http
POST /api/v1/identity/users/{id}/passkeys/reset
```

`UserDetailDto.PasskeyCount` and `ProfileDto.PasskeyCount` carry the number. The personal data export lists each passkey with name, date and backup state, never the public key. Deleting a user deletes the passkeys by cascade.

## Turning them off

The global setting `Account.Passkeys.Enabled` (group *Sign-in security*, default on) hides the button, the autofill, the account page and the entry in the app; the handlers answer `404`. Stored passkeys stay and work again when the setting is turned back on.

In the login mode *External* (only the external provider, e.g. Keycloak, signs in) passkeys are off as well: the provider has its own.

## Configuration

`Coworkee:Auth:Passkeys` binds the `IdentityPasskeyOptions` of ASP.NET Core Identity:

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

| Option | Meaning |
|---|---|
| `ServerDomain` | the relying party id, the host the passkeys belong to. Without it the host of the request is used: `localhost` in development, `auth.example.com` in production. Set it when the auth server runs behind a proxy without forwarded headers, so that requests carry an internal host. Passkeys of one domain do not work on another, so keep it when the server moves. |
| `AuthenticatorTimeout` | how long the browser waits for the authenticator (default 5 minutes) |
| `UserVerificationRequirement` | `required` (default). `preferred` or `discouraged` only change what the browser is asked for: the sign-in still refuses passkeys that did not verify the user |
| `ResidentKeyRequirement` | `preferred` (default), `required` or `discouraged` |
| `AttestationConveyancePreference`, `AuthenticatorAttachment` | passed on to the browser; no vendor list is checked |

The browser checks the origin of the page against the relying party, and the server compares it with the `Origin` header of the request. The auth server always has an own origin (development `https://localhost:7302`).

## Script and content security policy

`passkey.js` (embedded, `/auth-assets/passkey.js`) runs the browser side: it asks the page for the options, calls `navigator.credentials.create` or `get` and posts the result as a form field. It uses `PublicKeyCredential.parseCreationOptionsFromJSON` and `toJSON` where the browser has them and encodes base64url itself elsewhere. The policy of the auth server stays `script-src 'self'`.

## Upgrading

Passkeys live in the new table `cw.UserPasskeys` (key `CredentialId`, `Data` as JSON, cascade from `cw.Users`); existing apps add a migration. See the [changelog](../reference/changelog.md).
