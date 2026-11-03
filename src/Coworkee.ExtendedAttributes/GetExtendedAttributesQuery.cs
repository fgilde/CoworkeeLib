using Coworkee.Application.Messaging;
using Coworkee.Contracts.ExtendedAttributes;
using Coworkee.Core.Results;

namespace Coworkee.ExtendedAttributes;

public sealed record GetExtendedAttributesQuery(string EntityType, Guid EntityId) : IQuery<Result<IReadOnlyList<ExtendedAttributeDto>>>;
