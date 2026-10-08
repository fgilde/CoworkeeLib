using System.ComponentModel;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.OData.Transfer;

[AiTool("Lists rows of a data set (for example Products or Documents) with an optional OData filter like \"Name eq 'Acme'\" or \"contains(Name,'drill')\", " +
        "an order like \"Name desc\" and at most 50 rows. Answers with the rows and the total count.")]
public sealed record QueryDataQuery(
    [property: Description("The data set, e.g. Products.")] string EntitySet,
    [property: Description("OData $filter expression.")] string? Filter = null,
    [property: Description("OData $orderby expression.")] string? OrderBy = null,
    [property: Description("Rows to return, 1 to 50.")] int Top = 20,
    [property: Description("Rows to skip for paging.")] int Skip = 0) : IQuery<Result<DataPage>>;

public sealed record DataPage(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, long Total);
