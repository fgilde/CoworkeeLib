using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;

namespace Coworkee.Theming;

public sealed record GetCurrentTheme : IQuery<Result<ThemeDto>>;
