using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.ExtendedAttributes;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.ExtendedAttributes;

public sealed record GetExtendedAttributesQuery(string EntityType, Guid EntityId) : IQuery<Result<IReadOnlyList<ExtendedAttributeDto>>>;

/// <summary>Replaces every attribute of the entity with the given list.</summary>
public sealed record SetExtendedAttributesCommand(string EntityType, Guid EntityId, IReadOnlyList<ExtendedAttributeDto> Attributes)
    : ICommand<Result<IReadOnlyList<ExtendedAttributeDto>>>;

internal sealed class SetExtendedAttributesValidator : AbstractValidator<SetExtendedAttributesCommand>
{
    public SetExtendedAttributesValidator()
    {
        RuleFor(c => c.Attributes).Must(a => a.Select(x => x.Key.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == a.Count)
            .WithMessage("Every key may appear only once.");
        RuleForEach(c => c.Attributes).ChildRules(attribute =>
        {
            attribute.RuleFor(a => a.Key).NotEmpty().MaximumLength(100);
            attribute.RuleFor(a => a.Group).MaximumLength(100);
            attribute.RuleFor(a => a.Description).MaximumLength(500);
            attribute.RuleFor(a => a.ExternalId).MaximumLength(200);
            attribute.RuleFor(a => a.Json).Must(BeJson).When(a => a.Type == ExtendedAttributeType.Json).WithMessage("The value is not valid JSON.");
        });
    }

    private static bool BeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
