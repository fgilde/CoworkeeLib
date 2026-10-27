# Notification preferences, comment status, exports with progress and schedules, file tokens (L6-L9)

Driver: CoShipee (rebuild of a freight forwarding app). The original behaviour was read from its code; this spec says what the library adds and what stays app code.

## L9 Short-lived anonymous tokens

Original: `CreateViewTokenByIdAsync` caches a random token for 5 minutes (multi-use, the viewer asks several times), `GetStreamByTokenAsync(token, download)` streams the document anonymously, inline or as attachment. The export download token is the same idea but single use.

Coworkee has public files (`IsPublic`, permanent) only.

**Primitive** (`Coworkee.Infrastructure`, registered with the DbContext):

```csharp
public interface IAccessTokens
{
    // adds the token row; the caller's unit of work saves it
    string Issue(string purpose, Guid targetId, TimeSpan lifetime, bool singleUse);
    // null when unknown, of another purpose, expired or (single use) taken; taking is atomic
    Task<AccessTokenGrant?> RedeemAsync(string purpose, string token, CancellationToken ct);
}
public sealed record AccessTokenGrant(Guid TargetId, Guid? TenantId, Guid? UserId);
```

Table `cw.AccessTokens` (Hash = SHA-256 of the token, unique; Purpose; TargetId; TenantId; UserId; ExpiresAt; SingleUse; UsedAt). The token is 32 random bytes base64url; only its hash is stored. Single use takes the row with one conditional `ExecuteUpdate` (`UsedAt == null && ExpiresAt > now`), so two parallel downloads cannot both win. Expiry uses `TimeProvider`. Issuing removes rows expired more than a day ago (`ponytail:` cleanup on write, a retention target if issuing gets rare).

**Files**: `CreateFileToken(Guid Id)` (`Files.View` + folder grant, like `OpenFile`) -> `FileTokenDto(Token, Url, ExpiresAt)`, 5 minutes, multi-use. `GET|HEAD /api/v1/files/token/{token}/{name}?download=true` anonymous; inline by default, attachment with `download=true`. Route `POST /api/v1/files/{id}/token`.

**Record attachments**: `POST /api/v1/attachments/{type}/{id}/{slot}/{attachmentId}/token` (read check of the record) and anonymous `GET|HEAD /api/v1/attachments/token/{token}/{name}`.

Client: `ICoworkeeApi.CreateFileTokenAsync`, `IAttachmentsApi`/client member for attachments if present.

## L8 Exports

Exists: `GET /api/v1/data/{set}/export` (file, or `202` with a `DataTransferJob` for large exports; the owner follows the job over the user topic and downloads `/jobs/{id}/file` signed in).

Original adds: NDJSON progress stream (phase, percent, ready with a 5 minute one-time URL), the export goes on when the client leaves and is mailed, queued exports mailed to the requester and extra recipients, and scheduled recurring exports (daily, weekly, monthly by day or ordinal weekday, every N, time, time zone, recipients, file name, currency).

**Can existing parts carry it?**
- `ReportSubscription` is bound to a report (widgets, mail body with KPIs, unsubscribe per report). A list export is a set + query + columns + format. Sharing the table would make `ReportId` optional and branch every handler; no.
- `DataTransferJob` is one run with its result file. A schedule creates one job per run, so every run shows in the user's job list with its file. Yes, for runs.
- `ReportSchedule` / `ReportSchedules.Next` (Contracts) already do daily/weekly/monthly at a time of a time zone. They get `Every` (default 1) and `Week` (`MonthWeek?` First..Fourth, Last: monthly on the nth weekday). `ReportSubscription` stores both too, so the record means the same everywhere (UI for report subscriptions unchanged).

**Progress**: `DataExport.WriteAsync` takes an `IProgress<DataExportProgress>`: `counting` 0, `reading` 0-80 by rows read, `writing` 80-99, then done. Rows are read in chunks of 500 to report.

**Stream**: `GET /api/v1/data/{set}/export/stream?<same query as export>` -> `application/x-ndjson`, one JSON object per line:
`{"phase":"reading","percent":40}` ... `{"phase":"ready","percent":100,"url":"/api/v1/data/downloads/<token>","fileName":"Products.xlsx","jobId":"..."}` or `{"phase":"failed","message":"Export failed"}`.
The export is a `DataTransferJob` (status Running -> Completed) written in the request without the request's cancellation: when the client leaves, the export finishes and the file is mailed to the user (attachment). The `ready` URL is anonymous, single use, 5 minutes (`IAccessTokens`, purpose `data-export`).

**Mail**: OData cannot reference Mailing (Mailing depends on OData). OData defines `IExportMailer` (one method, `MailAsync(DataExportMail, ct)`); Mailing implements it with the new template `Data.Export` (23 languages, file attached). Without Mailing nothing is mailed (the job keeps the file).

**Queue and mail**: `POST /api/v1/data/{set}/export/mail` body `MailExportRequest(Query, Recipients, FileName)` -> 202 with the job; the job mails the owner and the recipients. Extra recipients need `Data.ExportSchedules`.

**Schedules**: entity `ExportSchedule` (`cw.ExportSchedules`): owner, `EntitySet`, `Query` (query string with `$filter`, `$search`, `$orderby`, `columns`, `format`), `Name`, `FileName`, `Recipients` (extra addresses), schedule fields of `ReportSchedule`, `Groups` (group selection when saved), `NextRunAt`, `LastRunAt`, `LastError`.
Requests (`Data.ExportSchedules`, own schedules only): `GetExportSchedules`, `SaveExportSchedule(Id?, request)`, `DeleteExportSchedule`, `RunExportSchedule` (now). Save checks the set's view permission. Recurring job `coworkee-export-schedules` (*/5): due schedules per tenant, each run as the owner (`ImpersonatedUser`) enqueues an `ExportJob` with mail; the next run moves on. A run whose owner lost the set's permission fails into the job and `LastError`.
Currency is app logic (money conversion of an app's columns): the query string is kept verbatim, apps read their own parameters.

Client: `IDataTransferApi` gets `StreamExportAsync` (progress callback, returns the ready URL), `MailExportAsync`, schedule members; `CoworkeeDataTable` export menu gets *Schedule export...* (dialog: name, frequency, every, weekday, day or ordinal weekday of month, time, time zone, format, file name, recipients) for holders of `Data.ExportSchedules`; page `data/export-schedules` (list, edit, run now, delete).

## L7 Comment status, validation hook, changed event

Original: comment status `None`, `Open`, `Closed` (extra property), set on add/edit or with `UpdateCommentStatus` (own permission); `ICommentValidator` runs before add, edit and status change (quotation: no comments on booked quotations); `CommentExtraPropertiesUpdatedEto` after add/edit with a status, its handler moves the quotation to Request while an Open comment exists, else back to Open. Extra properties `ReferenceNumber`, `TransportMode`.

Coworkee:
- `Comment.Status` (`CommentStatus { None, Open, Closed }`), `Comment.Properties` (string dictionary as JSON, at most 20 entries, keys 50, values 500 chars) for app values like a reference number.
- `AddCommentCommand` / `EditCommentCommand` take `Status` and `Properties` (edit keeps them when null). `SetCommentStatusCommand(Id, Status)` with the new permission `Social.Comments.SetStatus` (implies View), `PUT /api/v1/comments/{id}/status`. `CommentThreadDto.CanSetStatus`.
- Hook: `SocialOptions.ValidateComments(Func<IServiceProvider, CommentValidation, CancellationToken, Task<Error?>>)`; `CommentValidation(EntityType, EntityId, Text, Status, Properties, CommentId?, ParentId?, Action)` with `CommentAction { Add, Edit, Status, Delete }`. The first error is returned (no throw).
- Event: `Comment` becomes an `AggregateRoot` and raises `CommentChanged(CommentId, EntityType, EntityId, Status, Action)` on add, edit, status change and delete. It goes through the outbox (after the commit, as the actor), so a handler sees the committed thread: the quotation workflow is an `IDomainEventHandler<CommentChanged>` in the app.
- Client: `CoworkeeComments` shows a status chip; holders of SetStatus get a status menu per comment.

## L6 Notification preferences, done, counts, following, delayed mail

Exists: `Notification` (user, type, title/body with arguments, link, read), list (unread only, type, paging), unread count, read/unread/delete, daily digest, Web Push, realtime on the user topic.

Original: a notification has a category (Quotation, Booking, Comment, Shipment) and an event type (17); per user and event type a scope `Off`/`All`/`Subscribed` and channels in-app/e-mail (default Off, both channels); users follow entities (`EntitySubscription`: category + entity id); recipients = users allowed to see the entity whose setting is All, or Subscribed and following, minus the actor; inbox with counts per category (not done), done/undone (done marks read), search, open count; e-mails go through a job with a delay (`ShipmentNotificationDelayInMinutes`, default 1). The date-part debounce (skip when only the time of an estimated date moved) is the shipment handler's own logic: app code.

Coworkee:
- `Notification.Category` (null for uncategorised) and `DoneAt`. `GetNotifications` gets `Category`, `Done` (null all, false open, true done) and `Search` (title/body); `MarkNotificationDone(Id, Done)` (done marks read), `GET /api/v1/notifications/counts` -> `NotificationCountsDto(Open, Unread, Categories: {category: open count})`.
- Types: `services.AddNotificationTypes(n => n.Add("shipment.eta", "Shipment", "Estimated arrival changed", NotificationScope.Off, NotificationChannels.InApp | NotificationChannels.Email, emailDelay: TimeSpan.FromMinutes(1)).Followable<Shipment>("Shipment", link))`. Unregistered types keep today's behaviour (in-app only, digest).
- Preferences: `NotificationPreference` (`cw.NotificationPreferences`, user + type, scope, channels; row only when the user changed the default). `GetNotificationPreferences` (every registered type with category, name, the user's scope/channels and the defaults), `SaveNotificationPreference(Type, Scope, Channels)`.
- Following: `NotificationFollow` (`cw.NotificationFollows`, tenant, user, entity type, entity id, title, created; unique per user and entity). Entity types are opt-in (`Followable`), like Social: with an entity type the user must see the row through its OData set (403 without the set's permission, 404 for a hidden row). `FollowEntity(EntityType, EntityId, Title?)`, `UnfollowEntity`, `GetFollowState` -> `FollowStateDto(Following, CanFollow)`, `GetFollows` (own, newest first, with the registered link). The entity is public so apps can filter their lists ("followed only", "is followed" column).
- Publishing: `INotifier.PublishAsync(NotificationMessage)`: type, title, body, arguments, link, `Recipients` (addressed directly), `EntityType`/`EntityId` (its followers), `Audience` (users allowed to see it). Recipients: directly addressed users whose scope is not Off; audience users whose scope is All; followers whose scope is All or Followed (limited to the audience when one is given); the actor only when addressed directly. Per recipient the channels decide: in-app -> notification row, e-mail -> mail. `NotifyAsync` / `NotifyLocalizedAsync` are `PublishAsync` with `Recipients`.
- Mails: template `Notifications.Notification` (subject = title, body = greeting, title link, body; 23 languages). A type with `EmailDelay` writes a `PendingNotificationMail` (`cw.PendingNotificationMails`) due at now + delay; another notification of the same user, type and entity before that replaces it and moves the due time (debounce: one mail with the latest text once things are quiet). Recurring job `coworkee-notification-mails` (every minute) queues due mails in the recipient's language. Without a delay the mail is queued at once.
- Client: notifications page with tabs Open / Done / All, category chips with counts, search, done/undone action; a Preferences tab (types grouped by category: scope Off/All/Followed, channels in-app, e-mail); a Following tab (unfollow). `CoworkeeFollowButton` for detail pages. `ICoworkeeApi` members for all routes.

## Permissions and tenants

- Notification requests act on the current user's own rows; follows check the followed set. Follows, preferences, pending mails, export schedules, tokens carry the tenant; tokens redeem across tenants on purpose (anonymous) and the target is looked up with the token's tenant.
- New permissions: `Social.Comments.SetStatus`, `Data.ExportSchedules`.

## Tests

Real database tests in Coworkee.Files.Tests (token expiry with the fake clock, multi-use, other tenant, 403), Coworkee.OData.Tests (stream lines and ready token single use and expiry, mail on leave via a fake mailer, schedules: 403 without permission, tenant separation, next run with every/ordinal), Coworkee.Social.Tests (status permission 403, validator blocks, CommentChanged handled after commit), Coworkee.Notifications.Tests (preferences decide channels, follows 403/404 and tenant separation, done/counts/search, debounce timing with the fake TimeProvider), Coworkee.Reporting.Tests (`ReportSchedules.Next` with Every and Week), bUnit for the follow button, the notifications page tabs and the comment status menu.
