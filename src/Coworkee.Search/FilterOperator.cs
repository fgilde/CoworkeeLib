namespace Coworkee.Search;

public enum FilterOperator
{
    HasValue,
    HasNoValue,
    Equals,
    NotEquals,
    Contains,
    DoesNotContain,
    FuzzyEquals,
    FuzzyContains,
    GreaterThanOrEquals,
    LessThanOrEquals,
    Between,
    IdentifierEquals,
    IdentifierNotEquals,
}
