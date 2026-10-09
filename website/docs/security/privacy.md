# Privacy and personal data

Every module that stores data about a user contributes it through `IPersonalDataContributor`: one named JSON section for the export, and an erase step for account deletion.

```csharp
internal sealed class TaskPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "tasks";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken ct) =>
        await db.Set<TaskItem>().Where(t => t.AssigneeId == subject.UserId).Select(t => new { t.Title, t.DueAt }).ToListAsync(ct);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken ct) =>
        db.Set<TaskItem>().Where(t => t.AssigneeId == subject.UserId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AssigneeId, (Guid?)null), ct);
}

services.AddScoped<IPersonalDataContributor, TaskPersonalData>();
```

`PersonalDataSubject` carries user id, tenant and e-mail address, read before any contributor runs, so the order does not matter. A section that returns `null` is left out. Erasing runs in one transaction; direct `ExecuteDelete`/`ExecuteUpdate` calls write no audit entries with the old values.

## Built-in sections

| Section | Module | Erase |
|---|---|---|
| `profile` | Identity: name, e-mail, phone, address, picture, roles, groups, external logins | user deleted (roles, claims, logins, memberships by cascade), own permission grants removed, values in the user's own audit trail replaced by `***` |
| `settings` | Settings: values with user scope (secrets as `***`) | deleted |
| `notifications` | Notifications | deleted, with the digest state |
| `chat` | Chat: sent and received messages | both sides of the conversations deleted |
| `aiToolCalls` | AI: tool call log (the assistant keeps no chat history) | deleted |
| `mails` | Mailing: mails queued for the address, without bodies | deleted |
| `activity` | Auditing: what the user changed | kept; the trail names the actor only by id, which points to nobody afterwards |
| `social` | Comments, tags and ratings: own comments and ratings | deleted, replies to the comments too |
| `files` | Files: uploaded files (name, type, size) | kept, they belong to the organisation's folders |
| `signIns` | Auth server: granted clients | grants and tokens deleted, every session ends |

Database backups keep earlier data until they are rotated.

## Endpoints

| Endpoint | Who | Effect |
|---|---|---|
| `GET /api/v1/identity/me/personal-data` | the user | JSON object with one property per section |
| `POST /api/v1/identity/me/delete` with `{ "email": "…" }` | the user | erases everything; the address must match the account (`400` otherwise) |
| `DELETE /api/v1/identity/users/{id}` | `Identity.Users.Manage` | the same for another user of the tenant; only administrators delete administrators |

The last active administrator cannot be deleted (`409 identity.last_admin`). None of these requests is offered to the AI assistant.

The export is built synchronously: per-user data is small, and nothing is left behind in storage that would need its own expiry and clean-up. Move it into a background job once single users reach many megabytes.

## In the client

The account page has a **Privacy** tab: download the data as `personal-data.json`, or delete the account after typing the own e-mail address; the client signs out afterwards. Administrators find **Delete user** on the user detail page.
