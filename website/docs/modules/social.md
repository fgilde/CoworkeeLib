# Comments, tags and ratings

`Coworkee.Social` attaches comments, tags and star ratings to any entity, addressed by entity type and id, like the CMS kit of other frameworks. Entity types are opt-in: an app names the types that take each block.

```csharp
[DependsOn(typeof(CoworkeeSocialModule))]
public sealed class MyAppProductsModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeSocial(social => social
            .Comments<Product>("Products", id => $"/products/{id}")
            .Tags<Product>("Products")
            .Ratings<Product>("Products"));
}
```

With the entity class, the id is checked through the `DbContext`, so tenant filters apply; without it (`Comments("Products")`) any id is accepted. `AddCoworkeeSocial` may be called by several modules.

An optional hook decides on top of the permissions whether the user may see (`Write == false`) or change the social data of one entity, e.g. only products the user may see:

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

Moderate implies Create, Create implies View.

## Comments

Comments are threaded through `ParentId`. Deleting a comment deletes its replies. Every change is sent on the realtime topic `comments:{entityType}:{entityId}` (`SocialTopics.Comments`), so open threads reload live. With `Coworkee.Notifications` in the app, the creator of an audited entity and everyone who commented before get a notification, linked to the path from the registration.

## Tags

Each entity type has its own tag set per organisation. Names are matched without case; saving replaces the tags of an entity. `GET /api/v1/tags/{entityType}/entities?tag=red` returns the ids of the tagged entities, for filters.

## Ratings

One rating of 1 to 5 stars per user and entity; a second rating replaces the first. The summary carries average, count and the own rating.

| Endpoint | |
|---|---|
| `GET/POST /api/v1/comments/{entityType}/{entityId}` | the thread / comment with `{ "text", "parentId" }` |
| `PUT/DELETE /api/v1/comments/{id}` | edit / delete |
| `GET /api/v1/tags/{entityType}` | tag cloud with counts |
| `DELETE /api/v1/tags/{entityType}?tag=` | remove a tag from the set |
| `GET/PUT /api/v1/tags/{entityType}/{entityId}` | tags of an entity / replace with `{ "tags": [...] }` |
| `GET/PUT/DELETE /api/v1/ratings/{entityType}/{entityId}` | summary / rate with `{ "stars" }` / take back (`?userId=` for moderators) |

## Database

The module adds the tables `cw.Comments`, `cw.Tags`, `cw.EntityTags` and `cw.Ratings`; create a migration in the app after adding it.

## In the client

```razor
<CoworkeeRating EntityType="Products" EntityId="product.Id" />
<CoworkeeTags EntityType="Products" EntityId="product.Id" />
<CoworkeeComments EntityType="Products" EntityId="product.Id" />
```

The components ask the server what the user may do and hide the inputs otherwise. `CoworkeeTagCloud` lists the tag set and turns the chosen tag into an OData filter for a data table:

```razor
<CoworkeeTagCloud EntityType="Products" @bind-Filter="_tagFilter" @bind-Filter:after="() => _table.ReloadAsync()" />
<CoworkeeDataTable @ref="_table" T="Product" EntitySet="Products" Filter="@_tagFilter" />
```
