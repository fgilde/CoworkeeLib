using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Retention;
using Coworkee.Core.Results;

namespace Coworkee.Retention;

[AiTool(Exclude = true)]
[RequiresPermission(RetentionPermissions.Manage)]
public sealed record CountRetentionQuery(string Key, int Days) : IQuery<Result<long>>;
