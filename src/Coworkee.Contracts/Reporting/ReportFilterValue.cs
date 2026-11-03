using Coworkee.Contracts.Data;

namespace Coworkee.Contracts.Reporting;

public sealed record ReportFilterValue(string EntitySet, string GroupKey, string GroupLabel, string Label, string OData, FacetOperator Operator = FacetOperator.Or);
