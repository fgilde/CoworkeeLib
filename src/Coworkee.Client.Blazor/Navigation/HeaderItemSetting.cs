namespace Coworkee.Client.Blazor.Navigation;

/// <summary>One entry of the header setting; the position in the list is the item's place.</summary>
public sealed record HeaderItemSetting(string Id, HeaderPlacement Placement);
