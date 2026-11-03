namespace Coworkee.Contracts.Configuration;

/// <summary>A size in bytes; forms edit it in KB, MB or GB.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FileSizeAttribute : Attribute;
