# Comments, tags, ratings and reactions

`Coworkee.Social` attaches comments, tags, star ratings and emoji reactions to any entity, addressed by entity type and id, like the CMS kit of other frameworks. Entity types are opt-in: an app names the types that take each block.

```csharp
[DependsOn(typeof(CoworkeeSocialModule))]
public sealed class MyAppProductsModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeSocial(social => social
            .Comments<Product>("Products", id => $"/products/{id}")
            .Tags<Product>("Products")
            .Ratings<Product>("Products")
            .Reactions<Product>("Products")
            .CommentReactions());
}
```

With the entity class, the social data of a row is only there for users who see the row: when the type has an [OData set](../web/odata.md), the way that set shows it (the set's permission, the organisation, soft delete and every `IODataEntityFilter<T>` row filter); without a set, every row of the own organisation. Everyone else gets 404 on comments, tags, ratings and reactions and may not follow the realtime topics; tag counts and `tags/{entityType}/entities` only count visible rows. Without the entity class (`Comments("Products")`) any id is accepted. `AddCoworkeeSocial` may be called by several modules.

An optional hook decides on top of that whether the user may see (`Write == false`) or change the social data of one entity, e.g. for an entity type without an OData set:

```csharp
social.Authorize(async (services, access, ct) =>
    access.EntityId is not { } id || await services.GetRequiredService<IProductAccess>().CanSeeAsync(id, ct));
```

## Permissions

| Block | View | Create | Moderate |
|---|---|---|---|
| Comments | `Social.Comments.View` | write, edit and delete own comments | delete any comment |
| Tags | `Social.Tags.View` | tag entities, new tags join the set | remove a tag from the set |
| Ratings | `Social.Ratings.View` | rate, take back the own rating | remove the rating of anyone |
| Reactions | `Social.Reactions.View` | react, take back the own reaction | – |

Moderate implies Create, Create implies View.

`Social.Comments.SetStatus` (implies View) sets the status of any comment; authors change the status of their own comments with an edit.

## Comments

Comments are threaded through `ParentId`. Deleting a comment deletes its replies. Every change is sent on the realtime topic `comments:{entityType}:{entityId}` (`SocialTopics.Comments`), so open threads reload live. With `Coworkee.Notifications` in the app, the creator of an audited entity and everyone who commented before get a notification, linked to the path from the registration.

### Status, app values and checks

A comment has a `Status`: `None`, `Open` (a question or request that waits for an answer) or `Closed`. It is set when the comment is written (`CommentRequest.Status`), by its author with an edit, or by holders of `Social.Comments.SetStatus` with `PUT /api/v1/comments/{id}/status` (`SetCommentStatusCommand`). `Properties` keeps app values of a comment, such as a reference number (at most 20, names up to 50 and values up to 500 characters); an edit without them keeps them.

Apps check comments before they change with `ValidateComments`; the check sees the action, the entity, text, status and values, and its error is the answer (nothing is written):

```csharp
services.AddCoworkeeSocial(social => social
    .Comments<Quotation>("Quotations", id => $"/quotations/{id}")
    .ValidateComments(async (services, comment, ct) =>
        await services.GetRequiredService<AppDbContext>().Quotations.AnyAsync(q => q.Id == comment.EntityId && q.Status == QuoteStatus.Booked, ct)
            ? Error.Conflict("quotations.booked", "Comments cannot be added or changed on a booked quotation.")
            : null));
```

Every add, edit, status change and delete raises the domain event `CommentChanged(CommentId, EntityType, EntityId, Status, Action)`. It goes through the outbox, so an `IDomainEventHandler<CommentChanged>` runs after the commit, as the user who made the change, and reads the thread as it is now: a quotation goes to *Request* while one of its comments is Open and back otherwise.

## Tags

Each entity type has its own tag set per organisation. Names are matched without case; saving replaces the tags of an entity. `GET /api/v1/tags/{entityType}/entities?tag=red` returns the ids of the tagged entities, for filters.

## Ratings

One rating of 1 to 5 stars per user and entity; a second rating replaces the first. The summary carries average, count and the own rating.

## Reactions

Every user can put several emojis on an entity, each once; reacting with the same emoji again takes it back (at most 20 per user and entity). Reactions list the users per emoji, first reaction first, and change live on the topic `reactions:{entityType}:{entityId}` (`SocialTopics.Reactions`).

`CommentReactions()` turns them on for the comments of every entity type with comments, entity type `Social.Comment` (`SocialReactions.Comment`) with the comment's id. Who may see or write the comments of an entity may see or add reactions there, with the reaction permissions on top. The thread carries the reactions of its comments, they change live on the thread's comments topic and go when their comment is deleted.

| Endpoint | |
|---|---|
| `GET/POST /api/v1/comments/{entityType}/{entityId}` | the thread / comment with `{ "text", "parentId" }` |
| `PUT/DELETE /api/v1/comments/{id}` | edit / delete |
| `GET /api/v1/tags/{entityType}` | tag cloud with counts |
| `DELETE /api/v1/tags/{entityType}?tag=` | remove a tag from the set |
| `GET/PUT /api/v1/tags/{entityType}/{entityId}` | tags of an entity / replace with `{ "tags": [...] }` |
| `GET/PUT/DELETE /api/v1/ratings/{entityType}/{entityId}` | summary / rate with `{ "stars" }` / take back (`?userId=` for moderators) |
| `GET/POST /api/v1/reactions/{entityType}/{entityId}` | reactions with who reacted / toggle the own `{ "emoji" }` |

## Database

The module adds the tables `cw.Comments`, `cw.Tags`, `cw.EntityTags`, `cw.Ratings` and `cw.Reactions`; create a migration in the app after adding it.

## In the client

```razor
<CoworkeeRating EntityType="Products" EntityId="product.Id" />
<CoworkeeTags EntityType="Products" EntityId="product.Id" />
<CoworkeeReactions EntityType="Products" EntityId="product.Id" />
<CoworkeeComments EntityType="Products" EntityId="product.Id" />
```

The components ask the server what the user may do and hide the inputs otherwise. `CoworkeeReactions` shows a chip per emoji with its count (the own ones filled), who reacted on hover and a picker with `SocialReactions.Defaults` (👍 ❤️ 😂 🎉 😮 😢; `Emojis` sets others). With `CommentReactions()` the comments show the same row under each comment. `CoworkeeTagCloud` lists the tag set and turns the chosen tag into an OData filter for a data table:

```razor
<CoworkeeTagCloud EntityType="Products" @bind-Filter="_tagFilter" @bind-Filter:after="() => _table.ReloadAsync()" />
<CoworkeeDataTable @ref="_table" T="Product" EntitySet="Products" Filter="@_tagFilter" />
```
