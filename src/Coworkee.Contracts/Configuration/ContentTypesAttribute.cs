namespace Coworkee.Contracts.Configuration;

/// <summary>A list of MIME types ("application/pdf", "image/*"); forms edit it with a picker of common types.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ContentTypesAttribute : Attribute;
