using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[AiTool(Exclude = true)]
public sealed record OpenChatAttachmentQuery(Guid AttachmentId, bool Thumbnail = false) : IQuery<Result<ChatAttachmentContent>>;
