namespace Coworkee.Domain;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;
