using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Settings;

public sealed record GetClientSettings : IQuery<Result<IReadOnlyDictionary<string, string?>>>;
