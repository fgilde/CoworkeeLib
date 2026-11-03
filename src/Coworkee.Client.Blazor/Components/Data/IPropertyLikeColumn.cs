namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>A column without a property of the row type that the table treats like a property column of <see cref="ValueType"/>, named by its PropertyName.</summary>
internal interface IPropertyLikeColumn
{
    Type ValueType { get; }
}
