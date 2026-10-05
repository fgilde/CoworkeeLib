namespace Coworkee.OData;

public sealed record ODataEntityRegistration(Type EntityType, string EntitySet, string? Permission, IReadOnlyList<string> HiddenProperties);
