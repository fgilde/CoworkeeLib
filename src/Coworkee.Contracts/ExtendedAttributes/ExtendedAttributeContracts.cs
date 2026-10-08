using System.Text.Json.Serialization;

namespace Coworkee.Contracts.ExtendedAttributes;

[JsonConverter(typeof(JsonStringEnumConverter<ExtendedAttributeType>))]
public enum ExtendedAttributeType
{
    Decimal = 1,
    Text = 2,
    DateTime = 3,
    Json = 4,
}

public sealed class ExtendedAttributeDto
{
    public Guid? Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public ExtendedAttributeType Type { get; set; } = ExtendedAttributeType.Text;

    public string? Text { get; set; }

    public decimal? Decimal { get; set; }

    public DateTimeOffset? DateTime { get; set; }

    public string? Json { get; set; }

    public string? Group { get; set; }

    public string? Description { get; set; }

    public string? ExternalId { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed record SaveExtendedAttributesRequest(IReadOnlyList<ExtendedAttributeDto> Attributes);
