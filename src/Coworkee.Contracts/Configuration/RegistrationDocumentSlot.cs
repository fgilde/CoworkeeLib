using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Coworkee.Contracts.Configuration;

/// <summary>
/// One document the registration asks for. <see cref="Name"/> and <see cref="Description"/> are the texts in every language;
/// <see cref="Names"/> and <see cref="Descriptions"/> optionally translate them for single languages.
/// </summary>
public sealed class RegistrationDocumentSlot
{
    private const string Texts = "Shown to the user";
    private const string Translations = "Translations (optional)";
    private const string Upload = "Upload";

    [Display(Name = "Name", GroupName = Texts, Description = "The label of the upload field, e.g. Passport; uploaded files are filed under this name.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Hint", GroupName = Texts, Description = "Optional text under the field, e.g. Both sides, readable.")]
    public string? Description { get; set; }

    /// <summary>The name per language ("de": "Reisepass"); <see cref="Name"/> is the fallback and what stores file the document under.</summary>
    [Translations]
    [Display(Name = "Name in other languages", GroupName = Translations, Description = "Only for multilingual apps: users of a language listed here see this name instead of the one above.")]
    public Dictionary<string, string> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The description per language; <see cref="Description"/> is the fallback.</summary>
    [Translations]
    [Display(Name = "Hint in other languages", GroupName = Translations, Description = "Users of a language listed here see this hint instead of the one above.")]
    public Dictionary<string, string> Descriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Display(Name = "Required", GroupName = Upload, Description = "The registration cannot finish without this file.")]
    public bool Required { get; set; } = true;

    /// <summary>Accepted content types, wildcards allowed ("image/*"); empty accepts every type.</summary>
    [ContentTypes]
    [Display(Name = "Accepted file types", GroupName = Upload, Description = "Empty accepts every file type.")]
    public List<string> ContentTypes { get; set; } = [];

    /// <summary>Largest file in bytes; null or 0 for no limit.</summary>
    [FileSize]
    [Display(Name = "Largest file", GroupName = Upload)]
    public long? MaxSize { get; set; }

    /// <summary>The name in the current UI language ("de-AT", then "de"), else <see cref="Name"/>.</summary>
    public string DisplayName => InLanguage(Names) ?? Name;

    public string? DisplayDescription => InLanguage(Descriptions) ?? Description;

    public override string ToString() => DisplayName;

    private static string? InLanguage(Dictionary<string, string> texts) =>
        Text(texts, CultureInfo.CurrentUICulture.Name) ?? Text(texts, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    private static string? Text(Dictionary<string, string> texts, string culture) => texts.GetValueOrDefault(culture) is { Length: > 0 } text ? text : null;
}
