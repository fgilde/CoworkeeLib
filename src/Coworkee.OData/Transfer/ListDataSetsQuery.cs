using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.OData.Transfer;

[AiTool("Lists the data sets you may read with query_data, with the name and type of each field.")]
public sealed record ListDataSetsQuery : IQuery<Result<IReadOnlyList<DataSetInfo>>>;
