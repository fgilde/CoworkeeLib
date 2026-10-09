namespace Coworkee.Application.Messaging;

/// <summary>Marks an open generic handler interface of another package, so AddMessagingFromAssembly registers its implementations too.</summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class HandlerContractAttribute : Attribute;
