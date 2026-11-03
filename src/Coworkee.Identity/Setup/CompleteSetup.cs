using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Setup;

public sealed record CompleteSetup(CompleteSetupRequest Request) : ICommand<Result<SetupResultDto>>;
