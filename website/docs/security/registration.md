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
      { "Name": "Passport", "Description": "A scan of your passport", "ContentTypes": [ "image/*", "application/pdf" ], "MaxSize": 4000000 },
      { "Name": "Certificate", "Required": false, "ContentTypes": [ "application/pdf" ] }
    ]
  }
}
```

| Setting | Meaning |
|---|---|
| `RequireAddress` | street, zip code, city and country are required in step 2 |
| `AllowedEmails` | patterns with `*` an address must match to register; empty allows all |
| `Password` | ASP.NET Core Identity's `PasswordOptions`; they apply to every password, also the ones admins set |
| `RequireDocuments` | shows the document uploads |
| `Documents` | one slot per document: `Name`, `Description`, `Required` (default `true`), `ContentTypes` (wildcards like `image/*` or `application/vnd.openxmlformats-officedocument.*`, empty accepts all), `MaxSize` in bytes (empty or 0 for no limit) |

## Roles offered in the registration

A role with **Offered in the registration** (on the role page, `RoleRequest.SelectableForRegistration`, `SeedRole(..., SelectableForRegistration: true)`) appears in step 3. Sending a role that is not offered fails the step. System roles are never offered.

The flag is a column of `cw.Roles`. Apps with their own migrations add one after updating: `dotnet ef migrations add RoleSelectableForRegistration`.

## Where documents go

The auth server hands every file to an `IRegistrationDocumentStore` (in `Coworkee.Application.Registration`). It runs as the new user in the same unit of work that creates the account:

```csharp
public interface IRegistrationDocumentStore
{
    Task SaveAsync(RegistrationDocument document, CancellationToken cancellationToken);
}
```

- **`Coworkee.Files`** brings a default: the files land in *Registrations/{email}*. That folder has a single grant, for the new user through the role *Registration documents* (which may view files); only the user and holders of a global file grant, usually administrators, see it.
- **Your own store** wins over the default when your module registers it later (`services.AddScoped<IRegistrationDocumentStore, MyStore>()`). An app with its own document module stores them there, for example as documents of the type *Registration*, owned by the user and not public.

Required documents without any store stop the auth server with a clear error.

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
| `External:Mode` | `Internal` shows only the password form, `External` only the provider buttons (and hides the registration), `Both` shows both |
| `Login:AllowUserName` | the password form takes the user name as well as the email |
| `Login:AllowedEmails` | patterns an address must match to sign in, with a password or externally; empty allows all |

Accounts whose email is not confirmed cannot sign in with a password.

## External sign-in and existing accounts

On an external sign-in the auth server looks for the user by the linked login, then by email, then creates one:

- **Joining by email** needs a verified address: the provider sends `email_verified: true`, or the provider is configured with `TrustEmail` (`Coworkee:Auth:External:Providers:{name}:TrustEmail`). The Keycloak of the app host is trusted (`CoworkeeKeycloakOptions.TrustEmail`, default `true`), its realm belongs to the app.
- A local account whose own address is **not confirmed** is never joined: someone may have registered an address that is not theirs. The owner confirms it first.
- **New accounts** need `AutoProvision` and `Account.AllowRegistration`, and the address must match `Registration:AllowedEmails`. They follow `Account.RegistrationRequiresActivation`: an inactive account waits for an administrator, who gets a notification.

## No anonymous pages

`CoworkeeClientOptions.AllowAnonymous = false` sends every visitor who is not signed in from every page of the layout straight to the sign-in, with the page as return address. The setup wizard stays reachable. The BFF and the API keep their own rules either way.

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options => options.AllowAnonymous = false);
```

## Languages

The account pages follow the browser language (`Accept-Language`). The English text is the key; `Texts/de.json` in `Coworkee.AuthServer` holds German.
