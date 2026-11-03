namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>A tab of the group details added by the app; <see cref="Title"/> is translated, the component gets <c>[Parameter] Guid GroupId</c>.</summary>
public sealed record GroupDetailTab(Type Component, string Title, string? Permission);
