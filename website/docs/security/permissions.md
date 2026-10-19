# Permissions

Permissions are strings, defined by the modules, granted to roles, groups or single users, and checked on the server and in the UI.

## Defining

```csharp
internal sealed class CatalogPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(CatalogPermissions.GroupName, "Catalog")
            .Add(CatalogPermissions.Products.View, "View products")
            .Add(CatalogPermissions.Products.Edit, "Edit products", CatalogPermissions.Products.View);
}

services.AddSingleton<IPermissionDefinitionContributor, CatalogPermissionDefinitions>();
```

The definitions build the permission matrix in the role editor: grouped, searchable, with counts and a toggle per group.

![Role editor with the permission matrix](../assets/screenshots/role-permissions.png){ .shot }

## Checking on the server

| Where | How |
|---|---|
| A request | `[RequiresPermission(CatalogPermissions.Products.Delete)]` on the request type |
| Inside a handler | `await permissions.IsGrantedAsync(CatalogPermissions.Products.Edit, ct)` with `IPermissionChecker` |
| A single resource | `await permissions.IsGrantedAsync(permission, "Folder", folderId, ct)` |
| An endpoint group | `.RequireAuthorization()` plus the request attribute |
| An OData set | the permission passed to `AddODataEntity` |

Resource grants inherit along a hierarchy you describe with `IResourceHierarchy` (folders in folders, for example). `IResourceRestriction` takes access away again for certain roles, even from holders of a global grant.

## Acting as the system

Sometimes a module has to run a library command whatever the caller may do: the HR module keeps the group of a team in step when a manager edits the team, although the manager may not manage groups. `ISystemDispatcher` sends such a command as the system:

```csharp
await system.SendAsync(new SetGroupMembers(groupId, memberIds), [IdentityPermissions.Groups.Manage], ct);
```

The command runs in the caller's organisation, signed in without a user and with exactly the permissions you name (no roles, nothing more), and in a DI scope of its own, so the caller's pending changes are not saved with it. Its audit entries show *system*. `CurrentUserScope.BeginSystem(tenantId, permissions)` does the same without the own scope, e.g. in a background job for another organisation.

Use it only after your own code has decided that the caller may cause the change (`[RequiresPermission]` on your own command, a check on the record). Never pass on a request the caller built: it would let them do anything the named permissions allow. For work on behalf of a user, act as that user (`ImpersonatedUser`) instead.

## Checking in the UI

```razor
@attribute [Authorize(Policy = PermissionPolicy.Prefix + CatalogPermissions.Products.View)]

<PermissionGate Permission="@CatalogPermissions.Products.Create">
    <MudButton OnClick="CreateAsync">New product</MudButton>
</PermissionGate>
```

`PermissionGate` renders its content only with the permission; navigation entries and table buttons take a permission parameter and hide themselves. The client loads the effective permissions of the user after sign-in.

The UI only hides. The server decides.

## Host-only permissions

Some permissions serve the whole installation: `Features.Tenants`, `Features.Editions` and `Identity.Clients.Manage` ([clients and scopes](clients.md)). Every administrator holds them, but only the administrators of the system organisation can use them.

## Seeding roles

```csharp
seed.Roles.Add(new SeedRole("Product Manager", "Maintains products",
    [CatalogPermissions.Products.View, CatalogPermissions.Products.Create, CatalogPermissions.Products.Edit]));
```

See [Persistence](../fundamentals/persistence.md#migrations-and-seeding).
