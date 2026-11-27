using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.View)]
[Description("A grouped table on the server: a data set grouped by 1 to 8 fields with its values, values of up to 3 other data sets grouped by the same fields next to them, sorted by any column and paged.")]
public sealed record RunReportTableQuery(ReportTableRequest Request) : IQuery<Result<ReportTableResult>>;
