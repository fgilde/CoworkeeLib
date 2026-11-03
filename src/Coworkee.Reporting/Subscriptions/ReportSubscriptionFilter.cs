using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Subscriptions;

internal sealed record ReportSubscriptionFilter(ReportDateRange? DateRange, IReadOnlyList<ReportFilterValue>? Filters);
