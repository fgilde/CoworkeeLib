namespace Coworkee.Contracts.Configuration;

/// <summary>A list of MIME types ("application/pdf", "image/*"); forms edit it with a picker of common types.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ContentTypesAttribute : Attribute;

/// <summary>A cron expression (minute hour day month weekday); forms offer presets and describe it in words.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CronAttribute : Attribute;

/// <summary>A size in bytes; forms edit it in KB, MB or GB.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FileSizeAttribute : Attribute;
