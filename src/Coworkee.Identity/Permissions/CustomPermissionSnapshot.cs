using System.ComponentModel;
using Coworkee.Application.Authorization;

namespace Coworkee.Identity.Permissions;

// immutable: the cache hands out this instance itself instead of a copy, which tells the definition manager nothing changed
[ImmutableObject(true)]
internal sealed record CustomPermissionSnapshot(IReadOnlyList<PermissionDefinition> Definitions);
