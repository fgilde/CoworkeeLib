namespace Coworkee.Reporting;

/// <summary>What a report source computes its rows with: the request's services and the date range the widget asks for.</summary>
/// <param name="DateFrom">The lower bound of a date field in the widget's filter (inclusive), if any; a hint, the filter still applies to the rows.</param>
/// <param name="DateTo">The upper bound (exclusive for <c>lt</c>, as the report's date range sends it).</param>
public sealed record ReportSourceContext(IServiceProvider Services, DateOnly? DateFrom, DateOnly? DateTo);
