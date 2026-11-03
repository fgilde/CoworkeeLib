using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Privacy;

/// <summary>Everything the modules store about the signed-in user, one section per module.</summary>
[AiTool(Exclude = true)]
[NotWhileImpersonating]
public sealed record ExportMyPersonalData : IQuery<Result<IReadOnlyDictionary<string, object>>>;
