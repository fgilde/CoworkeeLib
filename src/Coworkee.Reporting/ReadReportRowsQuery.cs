using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.View)]
[Description("Reads a page of rows of a computed report source (one whose Computed flag is set), with the chosen fields, an OData filter and an order.")]
public sealed record ReadReportRowsQuery(ReportRowsRequest Request) : IQuery<Result<ReportRowsResult>>;
