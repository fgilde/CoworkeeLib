using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;

namespace Coworkee.Theming;

/// <summary>The themes that ship with the app, for anyone (setup offers them before there is an organisation).</summary>
public sealed record GetBuiltInThemes : IQuery<Result<IReadOnlyList<ThemeDto>>>;
