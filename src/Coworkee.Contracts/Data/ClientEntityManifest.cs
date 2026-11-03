namespace Coworkee.Contracts.Data;

/// <summary>
/// The entity sets a client may mirror. <see cref="Partition"/> separates the local copies of users, tenants and visibility scopes (e.g. the
/// group selection) on one device; <see cref="Owner"/> is the user and tenant alone: when it changes on a device, the client drops every copy.
/// </summary>
public sealed record ClientEntityManifest(string Partition, IReadOnlyList<ClientEntitySetInfo> Sets, string? Owner = null);
