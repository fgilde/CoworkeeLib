# User impersonation ("Log in as")

## Intent

Support staff and admins sign in as another user of their tenant to see and do what that user sees, without knowing the password, and switch back afterwards. CoShipee (the CargonerdsApp rebuild) needs it: the ABP app has it (ticket hand-over, start/stop pages, `impersonator_username` claim, revoke on stop, "the target may not hold more permissions than the impersonator"). It is generic, so it goes into the library.

## Goals

- A permission `Identity.Users.Impersonate` (implies `Identity.Users.View`, not implied by `Identity.Users.Manage`).
- "Log in as" on the user list row actions and the user detail header (Blazor admin), shown with the permission.
- While impersonating: every token carries the target as `sub` plus the impersonator (`impersonator` = user id, `impersonator_name` = display name); the Blazor app shows a clear banner/menu entry "Signed in as X on behalf of Y — Back to my account".
- Back to my account: switches the auth server sign-in back to the impersonator, revokes the refresh tokens and authorizations issued during the impersonation, and the BFF session continues as the impersonator.
- Rules (checked when the ticket is issued and again when it is redeemed):
  - caller has `Identity.Users.Impersonate`;
  - target is another, active user of the caller's tenant;
  - the caller is not impersonating already;
  - every permission the target holds (effective, all roles, groups, direct grants, computed) is one the caller holds too.
- Audit: `AuditEntry` gets `ImpersonatorId`; the audit trail interceptor writes it from `ICurrentUser.ImpersonatorId`. Start and stop are logged (ILogger, structured).
- Account management pages of the auth server (`/Account/Manage/*`: password, 2FA, passkeys, e-mail, personal data, applications) are forbidden while impersonating, so an impersonator cannot take over the target's credentials.

## Non-goals

- Cross-tenant impersonation (system tenant into a customer tenant). Later if needed.
- A separate SPA/public-client flow; the realtime FE of CoShipee gets its own adapter in the app.
- Instant death of access tokens issued during the impersonation: they are JWTs living at most `AccessTokenLifetime` (15 min); after stop the BFF no longer holds them. Documented.
- A dedicated security log page.

## Design

### Ticket (API host, Identity module)

- `StartImpersonation(Guid UserId) : ICommand<Result<ImpersonationTicketDto>>`, `[RequiresPermission(IdentityPermissions.Users.Impersonate)]`, validator (non-empty id). Handler checks the rules (permission subset: both sets from `PermissionChecker.GetGrantedForAsync`, so both get the same active-role narrowing; self → `identity.impersonate_self`; target inactive/other tenant → not found; already impersonating → `identity.already_impersonating`; target has more → `identity.impersonate_more_permissions` (403) listing nothing).
- `ImpersonationTicketDto(string Ticket)`. The ticket is `IDataProtector` (purpose `Coworkee.Impersonation.v1`) protected JSON `{ ImpersonatorId, TargetId, TenantId, ExpiresAt (now + 60 s) }`, via `ITimeLimitedDataProtector`. All hosts share the key ring (`DbXmlRepository`, application name `Coworkee`), so the auth server can read it.
- Endpoint `POST /api/v1/identity/users/{id}/impersonation` → `.ToHttpResult()`. `ICoworkeeApi.StartImpersonationAsync(Guid userId)`.

### BFF (Coworkee.Bff)

- `GET /bff/impersonate?ticket=...&returnUrl=/` → redirect to `{Authority}/Account/Impersonate?ticket=...&returnUrl={bff origin}/bff/login?returnUrl={returnUrl}` (returnUrl must be local).
- `GET /bff/stop-impersonation?returnUrl=/` → redirect to `{Authority}/Account/StopImpersonation?returnUrl={bff origin}/bff/login?returnUrl={returnUrl}`.
- `/bff/login` must re-run the challenge even when a cookie session exists, so the BFF session switches user (verify; if the OIDC handler short-circuits, sign out the BFF cookie first in these two flows by routing through a BFF endpoint that signs the local cookie out before redirecting).
- `BffUserDto` gets `ImpersonatorId` and `ImpersonatorName` (from the id-token claims).

### Auth server

- Page `/Account/Impersonate` (GET shows nothing interactive; it redeems directly, because the ticket is single-purpose, short-lived and bound to the signed-in impersonator):
  - requires the Identity cookie; its user must equal `ticket.ImpersonatorId`, else 403;
  - re-checks the rules;
  - signs the Identity cookie in as the target with extra claims `impersonator`, `impersonator_name` (`SignInWithClaimsAsync`, not persistent); the cookie gets a new `coworkee_sid` as on every sign-in;
  - redirects to `returnUrl` only if it is a registered client redirect origin or local (reuse the client redirect validation used elsewhere; never an open redirect).
- `CreatePrincipalAsync` copies `impersonator` and `impersonator_name` from the cookie principal (authorize) or from the refresh-token principal (refresh) into the token principal, destinations access + id token.
- While impersonating, the authorize endpoint creates an ad-hoc OpenIddict authorization for the target, client and scopes with property `coworkee_impersonation` = the cookie's sid, and attaches it (`SetAuthorizationId`), so refresh tokens hang on it.
- Page `/Account/StopImpersonation`: GET shows "Back to my account" (who is impersonated by whom), POST (antiforgery) does:
  - revoke the target's authorizations carrying `coworkee_impersonation` = current sid, and their tokens;
  - load the impersonator (must be active), sign the Identity cookie in as the impersonator (no extra claims);
  - redirect to the validated `returnUrl`.
- Razor Pages convention: folder `/Account/Manage` gets a filter that answers 403 while the cookie principal has an `impersonator` claim.

### API host / core

- `ICurrentUser.ImpersonatorId` (default interface member returning null); `HttpCurrentUser` reads the `impersonator` claim (ignored inside a `CurrentUserScope`).
- `AuditEntry.ImpersonatorId` (nullable Guid) + migration note in the changelog; `AuditTrailInterceptor` sets it. Audit DTO and audit log page show "on behalf of" when set.

### Blazor

- Users list row: icon button "Log in as" (`PermissionView Identity.Users.Impersonate`, not for the own row); user detail header: button. Both: confirm dialog, `StartImpersonationAsync`, then `Nav.NavigateTo($"bff/impersonate?ticket={Uri.EscapeDataString(t)}&returnUrl=/", forceLoad: true)`.
- `CoworkeeUserMenu`: when `ImpersonatorName` is set, a banner/chip in the app bar or the top of the menu "On behalf of {name}" and a menu item "Back to my account" → `bff/stop-impersonation?returnUrl=/` (force load).
- Texts through `L[...]`, translations in all `Coworkee.Localization/Localization/*.json` (the localization check must pass).

## Tests (must exist)

- Identity/API: ticket needs the permission (403), self rejected, other tenant/inactive → 404, target with more permissions → 403, already impersonating → rejected, ticket round-trips and expires.
- Auth server end to end (`OidcFlow`): admin signs in, gets a ticket, `/Account/Impersonate` with the admin's cookie → authorize + token → access token `sub` = target, `impersonator` = admin; refresh keeps `impersonator`; ticket with another signed-in user → 403; expired ticket → rejected; `/Account/Manage/*` → 403 while impersonating; stop → refresh token of the impersonation `invalid_grant`, authorize gives the admin again without `impersonator`.
- `HttpCurrentUser.ImpersonatorId` from the claim; audit entry written during impersonation carries `ImpersonatorId`.
- BFF: `/bff/user` exposes the impersonator; the two redirect endpoints reject non-local returnUrls.
- bUnit: "Log in as" hidden without the permission and on the own row; menu shows "Back to my account" only when impersonating.

## Left to the builder

Exact page markup, error codes' texts, where the banner sits, how the ad-hoc authorization is created with the OpenIddict 7 manager API.
