namespace Coworkee.Application.Events;

/// <summary>
/// An audience of the app's own, used as <c>EventTarget.Custom&lt;TResolver&gt;(args)</c>, e.g. the users of a shipment's parties. Resolved from the
/// services (registration optional: an unregistered resolver is created with its dependencies). The ids only narrow: users of other organisations
/// and inactive users are left out after it.
/// </summary>
public interface IAudienceResolver
{
    Task<IReadOnlyCollection<Guid>> ResolveAsync(object? args, CancellationToken cancellationToken);
}
