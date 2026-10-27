# Registration and sign-in rules

New users register themselves on the auth server with a wizard. It is server-rendered like the other account pages and needs no script:

1. **Account**: email and password (checked against the password rules).
2. **Personal data**: first and last name, phone and, when required, the postal address.
3. **Role**: only when roles are offered for registration; the user may pick several.
4. **Documents** or **Summary**: the summary of everything entered, plus one upload per document slot when documents are required. Browsers cannot keep a chosen file across pages, so the documents come with the final submit.

Every step checks its fields before the next one opens; *Back* keeps what was entered. Between the steps the input travels encrypted (ASP.NET Core data protection, two hours) in a hidden field, so the server keeps no session. The last submit checks all steps again.

## Settings admins change at runtime

Under **Administration > Settings > Accounts**:

| Setting | Default | Meaning |
|---|---|---|
| `Account.AllowRegistration` | `false` | shows the wizard and the *Create an account* link; lets external sign-ins create accounts |
| `Account.RegistrationRequiresActivation` | `true` | new accounts stay inactive until an administrator activates them; administrators get a notification (when `Coworkee.Notifications` runs in the auth host) |
| `Account.RegistrationRequiresEmailConfirmation` | `true` | the user confirms the address with the link in the mail before the first sign-in |
| `Auth.LoginMode` | `External:Mode` (`Both`) | the sign-in methods: password, external providers or both; under *Sign-in security*, defined only when an external provider is configured, see [Sign-in methods at runtime](#sign-in-methods-at-runtime) |

Defaults for a fresh installation come from configuration, for example `Coworkee:Settings:Defaults:Account.AllowRegistration = true`.

## Configuration

```json
"Coworkee": {
  "Registration": {
    "RequireAddress": true,
    "AllowedEmails": [ "*@example.com" ],
    "Password": { "RequiredLength": 8, "RequireUppercase": true, "RequireLowercase": true, "RequireDigit": true, "RequireNonAlphanumeric": false },
    "RequireDocuments": true,
    "Documents": [
      {
        "Name": "Passport", "Description": "A scan of your passport",
        "Names": { "de": "Reisepass" }, "Descriptions": { "de": "Ein Scan Ihres Reisepasses" },
        "ContentTypes": [ "image/*", "application/pdf" ], "MaxSize": 4000000
      },
      { "Name": "Certificate", "Required": false, "ContentTypes": [ "application/pdf" ] }
    ]
  }
}
```

| Setting | Meaning |
|---|---|
| `RequireAddress` | street, zip code, city and country are required in step 2 |
| `AllowedEmails` | patterns with `*` an address must match to register; empty allows all |
| `Password` | ASP.NET Core Identity's `PasswordOptions`; the defaults of the [password rule settings](clients.md#password-rules-and-lockout), which apply to every password, also the ones admins set |
| `RequireDocuments` | shows the document uploads |
| `Documents` | one slot per document: `Name`, `Description`, `Required` (default `true`), `ContentTypes` (wildcards like `image/*` or `application/vnd.openxmlformats-officedocument.*`, empty accepts all), `MaxSize` in bytes (empty or 0 for no limit); `Names` and `Descriptions` per language (`de-AT` before `de`) for what the user reads, `Name` and `Description` are the fallback and `Name` is what stores file the document under |

Admins edit the same values under **Settings > App settings > Registration**. Each document opens in a dialog with three sections: *Shown to the user* (name and hint, in every language), *Translations (optional)* (only for multilingual apps: a row per language, labeled *Translation for German* and so on, shown instead of the text above to users of that language) and *Upload* (required, accepted file types found by searching every known MIME type, or added as a whole group, largest file).

## Roles offered in the registration

A role with **Offered in the registration** (on the role page, `RoleRequest.SelectableForRegistration`, null keeps the value, `SeedRole(..., SelectableForRegistration: true)`) appears in step 3. Sending a role that is not offered fails the step. System roles are never offered. Turning the flag on needs `Identity.Users.Manage` and every permission the role grants (`identity.role_offer_more_permissions` otherwise), so a role manager cannot hand strangers more than he holds himself; the same holds for *Given to new users* ([default roles](../features/identity.md#owners-default-group-and-default-role)).

The flag is a column of `cw.Roles`. Apps with their own migrations add one after updating: `dotnet ef migrations add RoleSelectableForRegistration`.

## Where documents go

The auth server hands every file to an `IRegistrationDocumentStore` (in `Coworkee.Application.Registration`). It runs as the new user in the same unit of work that creates the account:

```csharp
public interface IRegistrationDocumentStore
{
    Task SaveAsync(RegistrationDocument document, CancellationToken cancellationToken);
}
```

- **`Coworkee.Files`** brings a default: the files land in *Registrations/{email}*. That folder has a single grant, for the new user through the role *Registration documents* (which may view files). Global file grants (`Files.View`, `Files.Manage`) do not reach the *Registrations* tree, neither in the file manager nor through OData; only the user and holders of `Files.Registrations.View` see it. Administrators have it like every permission; grant it to a role for others who check registrations.
- **Your own store** wins over the default when your module registers it later (`services.AddScoped<IRegistrationDocumentStore, MyStore>()`). An app with its own document module stores them there, for example as documents of the type *Registration*, owned by the user and not public.

The store has to live in the **auth server host**, where the registration runs; a store in the API host does not count. `Coworkee.Files` registers its default with `TryAdd`, so any store registered by the app wins. The app template with samples puts the registration documents into its *Documents* module (`MyAppRegistrationDocumentsModule` in the auth host); without samples, add `Coworkee.Files` or your own store to the auth host before you configure documents.

Configured documents without any store stop the auth server at startup with an `OptionsValidationException` that names the missing `IRegistrationDocumentStore`.

## Sign-in rules

```json
"Coworkee": {
  "Auth": {
    "External": { "Mode": "Both" },
    "Login": { "AllowUserName": true, "AllowedEmails": [ "*@example.com" ] }
  }
}
```

| Setting | Meaning |
|---|---|
| `External:Mode` | `Internal` shows only the password form, `External` only the provider buttons (and hides the registration), `Both` shows both; the default of the setting `Auth.LoginMode` |
| `Login:AllowUserName` | the password form takes the user name as well as the email |
| `Login:AllowedEmails` | patterns an address must match to sign in, with a password or externally; empty allows all |

Accounts whose email is not confirmed cannot sign in with a password.

### Sign-in methods at runtime

With at least one external provider configured, **Administration > Settings > Sign-in security** has the setting **Sign-in methods** (`Auth.LoginMode`, scope `Global`): *Password only* (`Internal`), *External providers only* (`External`) or *Password and external providers* (`Both`). `External:Mode` is its default; a stored value that is no mode name falls back to it. Without a provider the setting is not defined and the auth server always shows the password form, so nobody is locked out. The host of the settings API need not be the auth server: it reads `External:Mode` and the display names of the providers from its configuration, never their secrets (the Aspire app host passes both, see [Keycloak](../hosting/aspire.md#keycloak)).

An admin who saves *External providers only* while the provider is down locks everybody out, and the stored setting would win over any configuration change. The operator's way back is `Coworkee:Auth:External:ModeOverride`: when set, it wins over the setting (still the password form without a provider). Set `Coworkee__Auth__External__ModeOverride=Both` (or `Internal`) as an environment variable of the auth server and restart it; after the setting is fixed, remove the override.

## External sign-in and existing accounts

On an external sign-in the auth server looks for the user by the linked login, then by email, then creates one:

- **Joining by email** needs a verified address: the provider sends `email_verified: true`, or the provider is configured with `TrustEmail` (`Coworkee:Auth:External:Providers:{name}:TrustEmail`). The Keycloak of the app host is trusted (`CoworkeeKeycloakOptions.TrustEmail`, default `true`), its realm belongs to the app.
- A local account whose own address is **not confirmed** is never joined: someone may have registered an address that is not theirs. The owner confirms it first.
- **New accounts** need `AutoProvision` and `Account.AllowRegistration`, and the address must match `Registration:AllowedEmails`. They follow `Account.RegistrationRequiresActivation`: an inactive account waits for an administrator, who gets a notification.

### Completing an external sign-up

When the provider does not deliver everything the registration asks for, a new user goes through a short completion step on the auth server before an account or a session exists. It is the wizard without the account step: the email comes from the provider, the names are filled in from it.

- **Personal data** when the provider sent no first or last name, or `RequireAddress` is on,
- **Role** when roles are offered for registration,
- **Documents** when `RequireDocuments` is on.

Without any of these the account is created right away as before. The step reads the login from the external cookie once and then carries it in the encrypted wizard state. The last submit creates the account with the linked login and a confirmed email; without activation the user is signed in and sent back to the app, otherwise the page says that an administrator activates the account.

## No anonymous pages

`CoworkeeClientOptions.AllowAnonymous = false` sends every visitor who is not signed in from every page of the layout straight to the sign-in, with the page as return address. The setup wizard stays reachable. The BFF and the API keep their own rules either way.

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options => options.AllowAnonymous = false);
```

## Languages

The account pages, including the security pages under *Account/Manage* (change password, two-step verification), follow the browser language (`Accept-Language`). The English text is the key; `Texts/de.json` in `Coworkee.AuthServer` holds German. The document slots take their texts per language from `Names` and `Descriptions`.

The administrators' notification *New registration* is localizable: it is stored with the English texts as keys and the name and email as arguments, and every administrator reads it in the language of the app (`New registration` and `{0} ({1}) waits for activation.` in the app's texts).

## Updating

- `cw.Notifications` gets the column `Arguments` (`text[]`): `dotnet ef migrations add NotificationArguments`.
- Registration folders of `Coworkee.Files` are no longer visible with `Files.View` alone; roles that should see them need `Files.Registrations.View`.
