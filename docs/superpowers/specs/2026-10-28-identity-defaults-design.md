# Identity defaults: group owners, default group, default role

Driver: CoShipee parity gaps L20, L21, L37 (CargonerdsApp organisation units and roles).

## Design

### What the original does
- **Owners** (`OrganizationUnitOwnerUser`): a user/OU pair. Setting the owners replaces them and makes every owner a member; removing a member also removes the ownership. Listing needs the OU "manage users" permission.
- **Default OU** ("Default Organization Unit", found by name): a handler on "user created" adds a new user to it when the user is in no OU yet. The hub filters keep bookings, quotations and tags booked in it private to their booker.
- **Default role** (`IdentityRole.IsDefault`, set on DefaultCustomer by the role seed): registration and external sign-up give it to the new user.

### Model (one migration)
| Column | Meaning |
|---|---|
| `cw.GroupMembers.IsOwner` | the member owns the group. Owners are always members, so removing a member also ends the ownership |
| `cw.Groups.IsDefault` | the default group of the tenant (at most one, kept so by the handler) |
| `cw.Roles.IsDefault` | every new user gets the role (never a system role) |

### Requests, endpoints, `ICoworkeeApi`
| Request | Permission | Route | Client |
|---|---|---|---|
| `SetGroupOwners(id, userIds)`: sets the owners to exactly these users and adds the ones who are not members yet | `Groups.ManageMembers` | `PUT /groups/{id}/owners` | `SetGroupOwnersAsync` |
| `GetDefaultGroup`: the default group's id, or null | any signed-in user (it is only an id; apps need it in filters) | `GET /groups/default` | `GetDefaultGroupIdAsync` |
| `SetDefaultGroup(id?)`: makes a group the default, null clears it | `Groups.Manage` | `PUT /groups/default` | `SetDefaultGroupAsync` |
| `CreateRole`/`UpdateRole` with `RoleRequest.IsDefault` (`bool?`; null keeps the value, so older clients do not clear it) | `Roles.Manage` | existing | existing |

`GroupDto` gets `OwnerIds` and `IsDefault`, `RoleDto` gets `IsDefault`. The OData sets show the new columns: owners can be listed with `Users?$filter=Groups/any(m: m/GroupId eq {id} and m/IsOwner)`.

Server code uses `IGroupScope`: `DefaultGroupIdAsync()` and `OwnersAsync(groupId)` (owner user ids of a group of the tenant, e.g. as notification recipients: `notifier.NotifyAsync(await scope.OwnersAsync(id), ...)`).

### Every way a user is created
One `SaveChanges` interceptor (`NewUserDefaults`) instead of a hook per path: when users are added, it gives each of them the default roles of its tenant (and global ones) that are not already being added, and adds it to the tenant's default group unless it joins another group in the same save (as the original). It sees roles and groups that are added in the same save (the seed). So the admin's *Create user*, registration (wizard and external sign-up), SSO first sign-in, the seed, new tenants and any app import that goes through `CreateUser` or `UserManager` are covered, also app code that adds a `User` directly.
- `ponytail:` the interceptor runs after the audit trail, so the added memberships and role links are not separate audit entries; the user's creation is.

### Seed
- `SeedRole { IsDefault = true }` marks a seeded role default.
- `IdentitySeedOptions.DefaultGroup = "Default Organization Unit"`: the seed creates this root group, marks it default and makes the seeded users (and the setup admin) members; the development sync creates it when it is missing and no other group is default.
- At run time an administrator picks the default group on the groups page (by id through `SetDefaultGroup`).

### Group scope semantics
The default group is an ordinary group: its members have it in `MembershipsAsync`, `AccessibleAsync` and `ActiveAsync`, and the selection works as for every group. The library does **not** make rows in it private. An app that keeps records booked in the default group private to their creator adds that to its own filter:

```csharp
var ids = (await scope.ActiveAsync(ct)).ToList();
var standard = await scope.DefaultGroupIdAsync(ct);
return query.Where(s => EF.Parameter(ids).Contains(s.GroupId) && (s.GroupId != standard || s.CreatedBy == user.UserId));
```

### UI
- Groups page, Members tab: an *Owner* column; with `ManageMembers` a toggle per member (`SetGroupOwners`).
- Groups page, General tab: switch *Default group for new users* (`Groups.Manage`); the title shows a *Default group* chip.
- Roles: switch *Given to new users* on the role page and in the *New role* dialog, a chip on the roles list.

### Not done
- Owners of sub-groups are not owners of the parent (direct ownership only, like memberships for mail and approvers).
- No separate endpoint listing owner users: `GroupDto.OwnerIds`, the OData filter and `IGroupScope.OwnersAsync` cover listing.
- AirWorks admin apps and the TypeScript client types get the new fields with the next OpenAPI refresh from the demo.
