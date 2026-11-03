using System.ComponentModel;

namespace Coworkee.Cli.Spec;

public sealed class PropertySpec
{
    [Description("Property name; for a reference the navigation, its foreign key is <name>Id.")]
    public string Name { get; set; } = string.Empty;

    public PropertyType Type { get; set; } = PropertyType.String;

    [Description("Must be set; strings must not be empty.")]
    public bool Required { get; set; }

    [Description("Maximum length of a string, email or url (column and validation); without it a string column is text.")]
    public int? MaxLength { get; set; }

    [Description("Total digits of a decimal or money (default for money: 18).")]
    public int? Precision { get; set; }

    [Description("Digits after the point of a decimal or money (default for money: 2).")]
    public int? Scale { get; set; }

    [Description("Type enum: name of an enum of the spec.")]
    public string? Enum { get; set; }

    [Description("Type reference: the referenced entity, by name (Customer) or with its module (Crm.Customer).")]
    public string? Reference { get; set; }

    [Description("Type reference: text property of the referenced entity shown in lists and pickers (default: its display property).")]
    public string? ReferenceDisplay { get; set; }

    [Description("Type reference: what deleting the referenced row does (default: restrict).")]
    public DeleteBehavior? OnDelete { get; set; }

    [Description("Type reference: name of the collection the referenced entity gets (same module only), e.g. Orders on Customer.")]
    public string? Inverse { get; set; }

    [Description("Initial value of new rows: a number, true/false, an enum value or a text.")]
    public string? Default { get; set; }

    [Description("No two rows of a tenant share the value (unique index and check).")]
    public bool Unique { get; set; }

    [Description("Label in lists and forms (default: the name in words).")]
    public string? Label { get; set; }

    [Description("The text that names a row: in delete prompts, pickers and references (default: the first text property).")]
    public bool Display { get; set; }

    [Description("Part of the search box (default: true for texts, emails and urls).")]
    public bool? Searchable { get; set; }

    [Description("Offered as facet next to the list (bool, enum, reference, number or string).")]
    public bool Facet { get; set; }
}
