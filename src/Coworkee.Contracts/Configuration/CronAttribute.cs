namespace Coworkee.Contracts.Configuration;

/// <summary>A cron expression (minute hour day month weekday); forms offer presets and describe it in words.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CronAttribute : Attribute;
