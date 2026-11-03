using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;

namespace Coworkee.Theming;

public sealed record GetThemes : IQuery<Result<IReadOnlyList<ThemeDto>>>;
