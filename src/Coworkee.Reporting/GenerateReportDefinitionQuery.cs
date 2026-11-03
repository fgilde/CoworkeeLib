using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.Manage)]
[AiTool("Builds a report definition (widgets on a 12 column grid, values, groupings, filters) from a description with the app's AI model, checked against the data sets the user may read; pass the current definition to change it. Returns it unsaved: save it with create_report or update_report.", Name = "generate_report")]
public sealed record GenerateReportDefinitionQuery(string Prompt, ReportDefinition? Current = null) : IQuery<Result<ReportDefinition>>;
