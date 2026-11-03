using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.View)]
[Description("Aggregates a data set on the server: OData filter, group by up to two fields (dates by day, week, month, quarter or year) and count, sum, average, min, max or distinct count.")]
public sealed record RunReportQuery(ReportQueryRequest Request) : IQuery<Result<ReportQueryResult>>;
