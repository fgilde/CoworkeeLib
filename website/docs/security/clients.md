# Clients, sessions and password rules

The auth server is a full OpenID Connect provider. Besides the clients from the configuration, administrators of the system organisation manage further clients and scopes in the app, end sessions at once and set the password rules.

## Clients and scopes in the app

**Administration › Applications** lists every OpenID Connect client, **Administration › Scopes** the scopes they may ask for. Both need the permission `Identity.Clients.Manage` and work only in the system organisation, like the tenants page.

| Field | Meaning |
|---|---|
| Client id | the `client_id` the client sends |
| Type | `public` (browser and mobile apps, PKCE only, `S256`) or `confidential` (servers with a secret) |
| Consent | `implicit` signs in straight away, `explicit` asks the user once per scope set |
| Redirect addresses | where codes may go; their origins also enter the `form-action` of the content security policy |
| Addresses after sign-out | allowed `post_logout_redirect_uri` values |
| Allow refresh tokens | adds the `refresh_token` grant next to `authorization_code` |
| Scopes | `openid profile email roles offline_access` plus API scopes |
| Service client | adds the `client_credentials` grant (see below); without redirect addresses the client signs nobody in |

Scopes, redirect addresses and addresses after sign-out are chip lists ([`RenderAsChips`](../ui/data-table.md#chip-lists)): type a value and press Enter, or paste a list, which splits into chips at spaces, commas and line breaks. Scopes suggest the standard scopes (`openid`, `profile`, `email`, `roles`, `offline_access`, `phone`, `address`) and the scopes defined on **Administration › Scopes**, but take any name; `openid` is always there and cannot be removed. Values are checked like the server does: addresses have to be absolute, scope names may hold only letters, digits and `. _ : -`. Invalid values stay in the field with the error. The resources of a scope are a chip list too, with the resources of the other scopes as suggestions.

A confidential client gets a generated secret. It is shown once, right after creating it or after **New secret**; the database keeps only its hash.

Clients and API scopes from `Coworkee:Auth` are written to the OpenIddict tables on every start and marked as coming from the configuration. The pages show them with a lock and do not change them; change them in the configuration. Marked clients and scopes the configuration no longer names are removed at the start; those created in the pages stay. A scope's resources become the audiences of the access token, so a scope `reports` with the resource `reports_api` gives tokens an API with the audience `reports_api` accepts.

```http
GET    /api/v1/identity/clients
POST   /api/v1/identity/clients          → { id, clientSecret }
PUT    /api/v1/identity/clients/{id}     → { id, clientSecret } when it turned confidential
POST   /api/v1/identity/clients/{id}/secret
DELETE /api/v1/identity/clients/{id}
GET|POST /api/v1/identity/scopes, PUT|DELETE /api/v1/identity/scopes/{id}
```

The endpoints live in `CoworkeeAuthStoreModule`, which the API host loads together with the OpenIddict stores.

## Service clients

A confidential client with **Service client** calls APIs on its own, without a user, through the `client_credentials` grant. It acts in the system organisation with the permissions of the roles and of the permission list picked in its row of **Administration › Applications**:

```http
POST /connect/token
grant_type=client_credentials&client_id=reporting&client_secret=…&scope=myapp_api
```

- The access token names the client as `sub` and `client_id`, carries `tenant`, the role names and the listed permissions (`permission`).
- In the API, `ICurrentUser.UserId` is `null` and `ICurrentUser.ClientId` the client id; `IPermissionChecker` grants what the roles grant (changes count at once) plus the listed permissions (they count from the next token).
- System roles cannot be given to clients, and administrators can give only permissions they have themselves.
- Service clients have no session: there is no refresh token, a new token comes from the token endpoint again.

## Consent and authorized applications

For a client with explicit consent the auth server shows **Allow access** with the requested scopes. Allowing stores a permanent authorization; declining returns `consent_required` to the client.

Someone signed in as another user ([Log in as](../features/identity.md#log-in-as-another-user)) never consents for him: without the user's own authorization the client gets `consent_required`. Only clients with `AllowImpersonation` (`Coworkee:Auth:Clients:<n>:AllowImpersonation`; `AddWeb` sets it for the web client) serve an impersonation at all, the others answer `access_denied`.

On the account page (**Account security › Authorized applications**) users see every application with access, since when and with which scopes, and revoke it. Revoking ends the authorizations and tokens of that application; it has to ask for a sign-in again.

## App launcher

The root of the auth server opens **Your applications** for the signed-in user: a tile per application with logo, name, short description and a link to its home address. The brand at the top of every account page links there, and the page after signing out offers **Sign in again**.

The launcher lists clients that sign users in (authorization code), have a home address and **Show in the app launcher** on; service clients never appear. Administrators set **Home address**, **Logo address**, **Description** and **Show in the app launcher** on the clients page; clients from the configuration take `ClientUri`, `LogoUrl`, `Description` and `ShowInLauncher` (default on) of `Coworkee:Auth:Clients`. A configured client without own logo shows the logo of the account pages. In the app host, `AddWeb` sets the home address of the web client, so it appears without further configuration. Addresses have to be absolute http(s) addresses; the content security policy allows the origins of the logos as image sources.

## Branding of the account pages

The account pages take the app name from `Coworkee:Auth:DisplayName` and logo and colors from the default theme of the system organisation ([Theming](../modules/theming.md)); changes show within a minute. A theme without logo shows `Coworkee:Auth:LogoUrl`, an absolute address or a path on the auth server; the content security policy allows the origin of an absolute address as image source. In the app host, `options.LogoUrl = "/coworkee-icon.svg"` points the auth server at that path of the web app.

## Signing out everywhere and locking

The user detail page has **Sign out everywhere** and **Lock** (until a day or until unlocked); **Unlock** lifts a lock. All take effect at once:

```mermaid
sequenceDiagram
    participant Admin
    participant API
    participant DB
    participant Redis
    participant Other as Other API instances
    participant Client as User's browser
    participant BFF
    Admin->>API: POST /users/{id}/sign-out or /lock
    API->>DB: new security stamp, lockout end
    API->>DB: revoke OpenIddict tokens and authorizations
    API->>Redis: session change of the user
    Redis-->>Other: session change
    API-->>Client: SessionRevoked, then the hub connection closes
    Other-->>Client: the same for connections there
    Client->>BFF: POST /bff/logout, message, sign-in page
    Note over API,BFF: clients without a connection: the next API call answers 401, the BFF drops the session
```

- Access tokens carry `stamp`, a hash of the security stamp. The API compares it on every request and answers `401` once the stamp changed.
- The stamps are cached for a minute in HybridCache, with Redis as second level when the connection string `redis` is set. Every saved stamp change (whoever made it: administrator, password change, two-step setup) goes out as session change: through Redis pub/sub every API instance drops its cached stamp at once; without Redis only the own instance hears it and the others notice within the minute.
- Each instance keeps its realtime connections per user. On a session change it sends `SessionRevoked` (reason `signed-out`, `locked` or `password-changed`) to the connections whose token no longer counts and closes them a second later; `SessionGuard` in the layout signs out and tells the user why.
- Hubs of the app hear of it through `IRealtimePublisher`: every instance passes `SessionRevoked` on topic `user:{id}` with the `SessionRevokedPayload` (reason, `KeptSessionId`) to the publisher, once per change, also when the user has no connection to Coworkee's hub. A decorator of the publisher ends the user's connections of its own hub on that instance, except those whose token carries the kept `sid`. The publisher itself sends it to no group, so `RealtimeHub` clients get it only as described above.
- The BFF ends its cookie session when the API answers `401` to a request it sent a token with.
- Refresh tokens are revoked and, issued with the old stamp, refused anyway; the auth server's own sign-in cookie no longer counts at `/connect/authorize`.

The three actions need `Identity.Users.Manage` (`POST /api/v1/identity/users/{id}/sign-out`, `/lock` with an optional `until`, `/unlock`). Nobody ends his own session this way (`409 identity.own_session`; the user menu signs out), and only administrators can sign out or lock other administrators (`403 identity.admin_session_restricted`).

A [Log in as](../features/identity.md#log-in-as-another-user) session ends as well when the impersonator's stamp changes: when he is signed out everywhere, locked or deactivated, or changes his password, the next authorization or token refresh revokes the impersonation's tokens and signs the browser out.

### Changing the own password

A new password changes the stamp too, so changing it ends the other sessions: their tokens and refresh tokens are refused, their realtime connections close, their sign-in at the auth server no longer counts. The session that changed it keeps going: every sign-in at the auth server gets a session id that goes into its tokens (`sid`), and the user's record keeps that id with the stamp before (`KeptSession`). Its tokens stay valid until their next refresh, which issues them for the new stamp; any later stamp change voids the exception. `SessionRevoked` names that session (`SessionRevokedPayload.KeptSessionId`, the `sid`): `SessionGuard` ignores the message when it is its own session, and a single-page app ignores a `SessionRevoked` carrying the `sid` of its own access token the same way.

Apps without the account pages change it through the API, with the same rules: `PUT /api/v1/identity/me/password` with `{ currentPassword, newPassword }` answers `204`. A wrong current password answers `400` (field `CurrentPassword`) and counts towards the lockout, the password rules and the history answer `400` (field `NewPassword`), an account without password `409` (`account.no_password`). It is refused while signed in as another user (`auth.impersonating`) and with an API key (`account.own_sign_in_required`), and limited like the sign-in (rate limit policy `auth`). The calling session keeps its tokens through the `sid` of its access token; a token without one ends with the others. The auth server's sign-in cookie of that browser no longer counts, the next sign-in there asks for the new password.

Other modules hook in with `IUserSessionListener` (called after an administrator ended a user's sessions).

## Cross-origin calls (CORS)

Browser apps on another origin (the [single-page apps](../hosting/aspire.md#single-page-apps) of the app host) call the auth server (discovery, keys, token) and the API directly. `UseCoworkee` applies a CORS policy on both that allows exactly the origins of `Coworkee:Cors:Origins`, and none when the list is empty:

```json
"Coworkee": { "Cors": { "Origins": [ "https://spa.example.com" ] } }
```

- An entry is scheme, host and port; a path is ignored, `*` or anything that is no http(s) address stops the start.
- Any method and header, `Content-Disposition` readable, preflights cached for ten minutes. No credentials: the app sends its token in the `Authorization` header, never a cookie.
- Endpoints a browser has to call with credentials opt in with `.RequireCors(CoworkeeCors.WithCredentials)`, same origins plus `Access-Control-Allow-Credentials`. The realtime hub does, because SignalR's browser client negotiates with credentials.
- Other origins get no CORS headers, so the browser refuses the answer; calls from the same origin and from servers are not affected.
- The web host (BFF) removes every `Access-Control-*` header from its answers, also those it forwards from the API: there the session cookie is the credential, so no other origin may read what it answers.

## Password rules and lockout

The messages of ASP.NET Core Identity (password rules, taken e-mail address or user name, invalid codes and links) follow the request language: `LocalizedIdentityErrorDescriber` translates them through `ITextTranslator`, which the auth server feeds from its `Texts/{language}.json` and `Coworkee.Localization` from the module texts (`Localization/{language}.json`), so the admin pages of the API show them translated too.

**Administration › Settings › Sign-in security**, all at runtime:

| Setting | Default | Meaning |
|---|---|---|
| `Account.Password.RequiredLength` | 8 | shortest password, at least 1 |
| `Account.Password.RequiredUniqueChars` | 1 | a password needs so many different characters |
| `Account.Password.RequireDigit` | on | a password needs a digit |
| `Account.Password.RequireLowercase` | on | a password needs a lowercase letter |
| `Account.Password.RequireUppercase` | on | a password needs an uppercase letter |
| `Account.Password.RequireNonAlphanumeric` | on | a password needs a special character |
| `Account.Lockout.MaxFailedAttempts` | 10 | wrong passwords or codes in a row before the lockout |
| `Account.Lockout.Minutes` | 15 | how long that lockout lasts |
| `Account.Password.ExpiryDays` | 0 | after so many days the sign-in asks for a new password; 0 never |
| `Account.Password.History` | 0 | a new password may not be one of the last so many (up to 24 are kept) |

The defaults of the six password rules come from the configured Identity options, so `Coworkee:Registration:Password` stays the starting point of a deployment; what administrators save wins. They are `Global` settings, visible to the clients. `SettingsPasswordValidator` enforces them for every way a password is set: registration, reset, change, setting one as administrator and the first administrator at setup. A host that loads only the Identity module, without the Account module, keeps Identity's default validator and its configured options.

Next to every password field (registration, reset, change, new password at sign-in, the administrator's dialog, the setup wizard) a hint names the rules in force, e.g. *At least 12 characters, a digit, an uppercase letter.* `PasswordRules.Describe` builds it from the settings and a translation function. Before the first administrator exists nobody can read settings, so the setup status (`SetupStatusDto.PasswordRules`) carries the rules for the setup page.

Administrators tick **Change password at first sign-in** when they create a user, or **Change password at next sign-in** on the user detail page. After the sign-in, before the client gets tokens, the auth server shows **Choose a new password**; the same happens when the password expired. Users who sign in only with an external provider have no password and are never asked.
