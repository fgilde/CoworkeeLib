using Coworkee.Application.Messaging;
using Coworkee.Contracts.ExtendedAttributes;
using Coworkee.Core.Results;

namespace Coworkee.ExtendedAttributes;

/// <summary>Replaces every attribute of the entity with the given list.</summary>
public sealed record SetExtendedAttributesCommand(string EntityType, Guid EntityId, IReadOnlyList<ExtendedAttributeDto> Attributes)
    : ICommand<Result<IReadOnlyList<ExtendedAttributeDto>>>;
