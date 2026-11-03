using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.View)]
[Description("Lists the data sets reports can show, with their fields, kinds and enum values.")]
public sealed record GetReportSourcesQuery : IQuery<Result<IReadOnlyList<ReportSourceDto>>>;
