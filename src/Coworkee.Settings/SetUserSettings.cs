using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Settings;

public sealed record SetUserSettings(IReadOnlyDictionary<string, string?> Values) : ICommand<Result>;
