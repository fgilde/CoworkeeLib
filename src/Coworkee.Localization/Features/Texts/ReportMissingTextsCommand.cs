using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Localization.Features.Texts;

public sealed record ReportMissingTextsCommand(IReadOnlyList<string> Keys) : ICommand<Result>;
