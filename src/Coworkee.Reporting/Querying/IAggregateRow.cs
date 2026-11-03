namespace Coworkee.Reporting.Querying;

internal interface IAggregateRow
{
    object? Key { get; }

    double?[] Values { get; }
}
