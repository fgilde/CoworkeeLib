using System.ComponentModel;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.OData.Transfer;

[AiTool("Lists rows of a data set (for example Products or Documents) with an optional OData filter like \"Name eq 'Acme'\" or \"contains(Name,'drill')\", " +
        "an order like \"Name desc\", the fields to return and at most 50 rows. Answers with the rows and the total count; list_data_sets names the sets and fields.")]
public sealed record QueryDataQuery(
    [property: Description("The data set, e.g. Products.")] string EntitySet,
    [property: Description("OData $filter expression.")] string? Filter = null,
    [property: Description("OData $orderby expression.")] string? OrderBy = null,
    [property: Description("Rows to return, 1 to 50.")] int Top = 20,
    [property: Description("Rows to skip for paging.")] int Skip = 0,
    [property: Description("Comma separated fields to return, e.g. Id,Name; empty: all.")] string? Select = null) : IQuery<Result<DataPage>>;

[AiTool("Lists the data sets you may read with query_data, with the name and type of each field.")]
public sealed record ListDataSetsQuery : IQuery<Result<IReadOnlyList<DataSetInfo>>>;

public sealed record DataSetInfo(string Name, IReadOnlyDictionary<string, string> Fields);

public sealed record DataPage(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, long Total);
