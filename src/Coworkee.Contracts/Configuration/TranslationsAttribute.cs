namespace Coworkee.Contracts.Configuration;

/// <summary>A text per language (<c>Dictionary&lt;string, string&gt;</c>, culture to text); forms edit one row per language.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TranslationsAttribute : Attribute;
