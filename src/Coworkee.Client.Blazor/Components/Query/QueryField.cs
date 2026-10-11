using System.Text.Json.Serialization;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>
/// A field the query builder offers. <see cref="Name"/> is the path (<c>Customer.Name</c> or <c>Customer/Name</c>); <see cref="Options"/> or
/// <see cref="Lookup"/> turn the value editor into a choice, e.g. enum members or the rows of a referenced set.
/// </summary>
public sealed record QueryField(string Name, string Label, QueryFieldType Type)
{
    /// <summary>Fixed choices, such as enum members with their display names.</summary>
    public IReadOnlyList<QueryOption>? Options { get; init; }

    /// <summary>Searches choices as the user types, e.g. customers by name; the value is the option's <see cref="QueryOption.Value"/>.</summary>
    public Func<string?, CancellationToken, Task<IReadOnlyList<QueryOption>>>? Lookup { get; init; }

    /// <summary>Offers "is empty" and "is not empty".</summary>
    public bool Nullable { get; init; }

    /// <summary>A heading the field picker groups fields under.</summary>
    public string? Category { get; init; }

    /// <summary>Narrows the operators; <c>null</c> offers all that fit <see cref="Type"/>.</summary>
    public IReadOnlyList<QueryOperator>? Operators { get; init; }

    public bool HasChoices => Options is { Count: > 0 } || Lookup is not null;
}

public sealed record QueryOption(string Value, string Label);

[JsonConverter(typeof(JsonStringEnumConverter<QueryFieldType>))]
public enum QueryFieldType
{
    Text,
    Number,
    Boolean,

    /// <summary>A day without time (<c>DateOnly</c>).</summary>
    Date,

    /// <summary>A point in time, compared by whole days in the user's offset.</summary>
    DateTime,
    Enum,
    Guid,
}
