# Privacy and personal data

Every module that stores data about a user contributes it through `IPersonalDataContributor`: one named JSON section for the export, and an erase step for account deletion.

```csharp
internal sealed class TaskPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "tasks";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken ct) =>
        await db.Set<TaskItem>().Where(t => t.AssigneeId == subject.UserId).Select(t => new { t.Title, t.DueAt }).ToListAsync(ct);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken ct) =>
        db.Set<TaskItem>().Where(t => t.AssigneeId == subject.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.AssigneeId, (Guid?)PersonalDataSubject.ErasedUserId), ct);
}

services.AddScoped<IPersonalDataContributor, TaskPersonalData>();
```

`PersonalDataSubject` carries user id, tenant and e-mail address, read before any contributor runs, so the order does not matter. A section that returns `null` is left out. Erasing runs in one transaction; direct `ExecuteDelete`/`ExecuteUpdate` calls write no audit entries with the old values.

## The rules of an erase

- **His own data goes**: what exists only because of him and serves only him (profile, settings, notifications, keys, sessions, private reports, his jobs and schedules, his chat messages and comments).
- **Records of the organisation stay**, with `PersonalDataSubject.ErasedUserId` (`00000000-0000-0000-0000-000000000000`) where his id stood: "a user who was erased", unlike `null`, "nobody". Free text he wrote into them (workflow comments) goes.
- **Nothing runs as him any more**: report subscriptions, scheduled exports and running exports stop, webhook endpoints he owned are switched off, his address leaves the recipients of other schedules, approvals waiting for him move on, his tokens, API keys, links and the cached session stamp go.
- **Organisations**: a user id belongs to one user of one organisation (`Users.TenantId`), so rows that carry his id are his wherever they are (a host administrator's id on the organisations he created). Rows found by his **e-mail address** count only in his organisation: the same address may belong to another user of another organisation.

After the contributors the library saves once and then runs its own step (`PersonalDataReferences`, Coworkee.Infrastructure) on the whole database:

| What | Rule |
|---|---|
| `CreatedBy`, `ModifiedBy`, `DeletedBy` of every table (yours too) | his id becomes the tombstone |
| `AuditEntries.ActorId`, `ImpersonatorId` | his id becomes the tombstone |
| Audit entries of the records erased with him (his user, rows keyed by his id, records he created that no longer exist) | every recorded value becomes `***`; action, time, record type and id stay |
| All other audit entries | his id (also in compact form) and his e-mail address inside recorded values are replaced (tombstone, `***`) |
| `EntitySnapshots` | snapshots of the records erased with him are deleted; in the others his id and address are replaced; `CreatedBy` becomes the tombstone |
| `OutboxMessages.ActorId`, `ImpersonatorId` | his id becomes the tombstone (pending messages still run, as nobody in particular) |
| `AccessTokens` (anonymous links he issued) | deleted |

Then the cached security stamp is dropped on every instance, so tokens of the user stop at once.

### The audit trail

The trail answers *what happened to which record, when, and done by a person or by the system*. That stays: it is the organisation's record, and deleting it would let an erase hide changes. *Who* the person was, and what the trail recorded about him, goes: his id becomes the tombstone, and the values of his own records (his user, his memberships, the comments, messages, settings and other records he created that are gone) become `***`. Values of records that stay (an order he edited) stay, with his id and address taken out. Names of the user typed as free text by others are not searched for.

## Built-in sections

| Section | Module | Exported | Erase |
|---|---|---|---|
| `profile` | Identity | name, e-mail, phone, address, picture, two-step verification on or off, roles, groups, external logins, passkeys (name, date, backup state), `claims`, own `permissionGrants`, `resourcePermissions` (type, id, role), `selectedGroups` | user deleted with roles, claims, logins, tokens, passkeys, memberships and group selections (cascade); own permission and resource grants deleted |
| `sessions` | Identity | [sessions](clients.md#sessions) (app, device, browser, user agent, IP address, start, last activity, end and reason) | his sessions deleted; sessions of others he signed in as or ended keep that with the tombstone |
| `apiKeys` | Identity | [API keys](api-keys.md) (name, prefix, permissions, dates, last use), never the hash | his keys deleted; keys of others he revoked keep the revocation with the tombstone |
| `signIns` | Auth server | granted clients | grants and tokens deleted, every session ends |
| `settings` | Settings | values with user scope (secrets as `***`) | deleted |
| `notifications` | Notifications | notifications, Web Push devices, preferences, followed records, mails waiting for their delay, last digest | deleted, in every organisation |
| `mails` | Mailing | mails queued for the address in his organisation, without bodies | deleted with their attachments, in his organisation |
| `chat` | Chat | `messages` (sent anywhere, received in direct chats), `memberships`, `reactions`, `attachments` (listed, without content) | sent messages with their files, reactions and memberships deleted; direct chats deleted with both sides; group chats he created stay with the tombstone as creator |
| `social` | Comments, tags, ratings, reactions | own comments, ratings and reactions | deleted, replies to the comments too |
| `aiToolCalls`, `aiConversations` | AI | tool calls (tool, channel, input, result), conversations | deleted |
| `activity` | Auditing | what the user changed, himself or signed in as someone else (`asOtherUser`) | trail kept as described above |
| `files` | Files | uploaded files (name, type, size) | kept with the tombstone as uploader; his registration documents (`Registrations/{user}`) deleted with their content and folder |
| `attachments` | Record attachments | files he attached (record, slot, name, type, size) | kept with the tombstone as uploader |
| `reports` | Reporting | `reports` he owns, `favorites`, `subscriptions` (schedule, filter), `dashboard` | private reports, favourites, subscriptions and the dashboard choice deleted; shared reports stay for their audience, owned by the tombstone (nobody changes them any more) |
| `dataTransfers` | Import and export | `jobs` (files listed with name and size), `schedules` he owns, `mailedBy` (schedules of others that mail him) | jobs deleted with their files (a running export stops), his schedules deleted, his address removed from the others' recipients in his organisation |
| `webhooks` | Webhooks | endpoints he saved last (URL, events, state) | kept, switched off and owned by the tombstone, since deliveries ran with his permissions; saving the endpoint again makes the administrator its owner |
| `workflows` | Workflows | `steps` he ran (with comments), `decisions`, `requested` approvals | history and decisions kept with the tombstone, his comments removed; he leaves pending approvals, and one then waiting for nobody is overdue at once (the deadline job moves it to its timeout state or adds the escalation roles; one already escalated is cancelled) |
| none | Retention | | clean-ups he started by hand keep the tombstone as `LastRunBy` |

`tests/Coworkee.ArchitectureTests/PersonalDataCoverageTests` lists every column of the library's tables that names a user with the contributor that handles it; a new column fails the test until it is covered.

## What stays

| Data | Why | Until |
|---|---|---|
| The audit trail and snapshots, as described above | integrity of the organisation's records | the [retention](../modules/retention.md) targets `audit` and `snapshots` |
| Payloads of processed outbox messages (domain events) | delivery log of the event bus | the target `outbox` |
| Webhook delivery logs (bodies sent to receivers) | what left the system to a third party | the target `webhook-deliveries` |
| Notifications, chat messages and comments of others that name him in their text | they belong to the other users | the target `notifications`, or as long as the others keep them |
| Elsa workflow instances (their input holds the user id) | running workflows would break | Elsa's own clean-up of finished instances |
| Search index documents | the index belongs to the app (`ISearchIndex`) | the app's contributor removes them |
| Database backups | backups are restored as a whole | they are rotated |

## Your own tables

- `CreatedBy`, `ModifiedBy` and `DeletedBy` (of type `Guid`) are handled for every table of the model; nothing to do.
- Every other column that holds a user id, a name or an address of a user needs a contributor in your module: export it, delete what is his own, put `PersonalDataSubject.ErasedUserId` into the rest. Stop what runs as the user (own jobs, subscriptions).
- Rows found by the e-mail address: limit them to `subject.TenantId`.
- Search index documents with personal data: remove them in the contributor.

## Endpoints

| Endpoint | Who | Effect |
|---|---|---|
| `GET /api/v1/identity/me/personal-data` | the user | JSON object with one property per section, sorted by name |
| `POST /api/v1/identity/me/delete` with `{ "email": "…" }` | the user | erases everything; the address must match the account (`400` otherwise) |
| `DELETE /api/v1/identity/users/{id}` | `Identity.Users.Manage` | the same for another user of the tenant; only administrators delete administrators (`403 identity.admin_role_restricted`), nobody deletes a user holding a permission he lacks (`403 identity.user_more_permissions`) |

The last active administrator cannot be deleted (`409 identity.last_admin`). None of these requests is offered to the AI assistant. Someone signed in as another user ([Log in as](../features/identity.md#log-in-as-another-user)) can neither export the user's data nor delete the account (`403 auth.impersonating`).

The export is built synchronously: per-user data is small, and nothing is left behind in storage that would need its own expiry and clean-up. Move it into a background job once single users reach many megabytes. Files are listed, not included: uploads and attachments belong to the organisation's records, export files hold the organisation's rows.

## In the client

The profile page has a **Privacy** tab: download the data as `personal-data.json`, or delete the account after typing the own e-mail address; the client signs out afterwards. Administrators find **Delete user** on the user detail page.
