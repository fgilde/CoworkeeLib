namespace Coworkee.Search;

/// <summary>A condition on one field; several values of Equals/IdentifierEquals mean "any of".</summary>
public sealed record SearchFilter(string Field, FilterOperator Operator, IReadOnlyList<string>? Values = null);
