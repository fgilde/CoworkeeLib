# Clients, sessions and password rules

The auth server is a full OpenID Connect provider. Besides the clients from the configuration, administrators of the system organisation manage further clients and scopes in the app, end sessions at once and set the password rules.

## Clients and scopes in the app

**Administration › Applications** lists every OpenID Connect client, **Administration › Scopes** the scopes they may ask for. Both need the permission `Identity.Clients.Manage` and work only in the system organisation, like the tenants page.

| Field | Meaning |
|---|---|
| Client id | the `client_id` the client sends |
| Type | `public` (browser and mobile apps, PKCE only) or `confidential` (servers with a secret) |
| Consent | `implicit` signs in straight away, `explicit` asks the user once per scope set |
| Redirect addresses | where codes may go; their origins also enter the `form-action` of the content security policy |
| Addresses after sign-out | allowed `post_logout_redirect_uri` values |
| Allow refresh tokens | adds the `refresh_token` grant next to `authorization_code` |
| Scopes | `openid profile email roles offline_access` plus API scopes |

A confidential client gets a generated secret. It is shown once, right after creating it or after **New secret**; the database keeps only its hash.

Clients and API scopes from `Coworkee:Auth` are written to the OpenIddict tables on every start and marked as coming from the configuration. The pages show them with a lock and do not change them; change them in the configuration. A scope's resources become the audiences of the access token, so a scope `reports` with the resource `reports_api` gives tokens an API with the audience `reports_api` accepts.

```http
GET    /api/v1/identity/clients
POST   /api/v1/identity/clients          → { id, clientSecret }
PUT    /api/v1/identity/clients/{id}     → { id, clientSecret } when it turned confidential
POST   /api/v1/identity/clients/{id}/secret
DELETE /api/v1/identity/clients/{id}
GET|POST /api/v1/identity/scopes, PUT|DELETE /api/v1/identity/scopes/{id}
```

The endpoints live in `CoworkeeAuthStoreModule`, which the API host loads together with the OpenIddict stores.

## Consent and authorized applications

For a client with explicit consent the auth server shows **Allow access** with the requested scopes. Allowing stores a permanent authorization; declining returns `consent_required` to the client.

On the account page (**Account security › Authorized applications**) users see every application with access, since when and with which scopes, and revoke it. Revoking ends the authorizations and tokens of that application; it has to ask for a sign-in again.

## Signing out everywhere and locking

The user detail page has **Sign out everywhere** and **Lock** (until a day or until unlocked); **Unlock** lifts a lock. All take effect at once:

```mermaid
sequenceDiagram
    participant Admin
    participant API
    participant DB
    participant Hub as Realtime hub
    participant Client as User's browser
    participant BFF
    Admin->>API: POST /users/{id}/sign-out or /lock
    API->>DB: new security stamp, lockout end
    API->>DB: revoke OpenIddict tokens and authorizations
    API->>Hub: SessionRevoked on user:{id}
    Hub-->>Client: SessionRevoked
    Client->>BFF: POST /bff/logout, message, sign-in page
    Note over API,BFF: clients without a connection: the next API call answers 401, the BFF drops the session
```

- Access tokens carry `stamp`, a hash of the security stamp. The API compares it on every request (cached, dropped on every change of the user) and answers `401` once the stamp changed.
- The BFF ends its cookie session when the API answers `401` to a request it sent a token with.
- Refresh tokens are revoked and, issued with the old stamp, refused anyway; the auth server's own sign-in cookie no longer counts at `/connect/authorize`.
- `SessionRevoked` reaches the open clients on the user's topic; `SessionGuard` in the layout signs out and tells the user why.

A new password changes the stamp too, so changing it ends the other sessions. Administrators cannot end their own session this way, and only administrators can sign out or lock other administrators.

Other modules hook in with `IUserSessionListener` (called after an administrator ended a user's sessions).

## Password rules and lockout

**Administration › Settings › Sign-in security**, all at runtime:

| Setting | Default | Meaning |
|---|---|---|
| `Account.Lockout.MaxFailedAttempts` | 10 | wrong passwords or codes in a row before the lockout |
| `Account.Lockout.Minutes` | 15 | how long that lockout lasts |
| `Account.Password.ExpiryDays` | 0 | after so many days the sign-in asks for a new password; 0 never |
| `Account.Password.History` | 0 | a new password may not be one of the last so many (up to 24 are kept) |

Administrators tick **Change password at first sign-in** when they create a user, or **Change password at next sign-in** on the user detail page. After the sign-in, before the client gets tokens, the auth server shows **Choose a new password**; the same happens when the password expired. Users who sign in only with an external provider have no password and are never asked.

## Branding of the account pages

The account pages take the app name from `Coworkee:Auth:DisplayName` and logo and colors from the default theme of the system organisation ([Theming](../modules/theming.md)); changes show within a minute.
