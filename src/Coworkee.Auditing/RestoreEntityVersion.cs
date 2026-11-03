using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Auditing;

public sealed record RestoreEntityVersion(string Type, Guid Id, int Revision) : ICommand<Result>;
