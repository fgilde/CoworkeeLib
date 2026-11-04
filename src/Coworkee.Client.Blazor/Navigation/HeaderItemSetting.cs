using System.Text.Json.Serialization;
using Coworkee.Client.Blazor.Security;

namespace Coworkee.Client.Blazor.Navigation;

/// <summary>
/// One entry of the header setting; the position in the list is the item's place. With <see cref="Permissions"/> the item shows only to
/// users who have any or all (<see cref="Match"/>) of them.
/// </summary>
public sealed record HeaderItemSetting(
    string Id,
    HeaderPlacement Placement,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Permissions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] PermissionMatch Match = PermissionMatch.Any);
