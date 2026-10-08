using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Realtime;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Users.Profile;

/// <summary>Tells the organisation that a user's name or picture changed, when the realtime module runs.</summary>
internal sealed class UserChanges(IServiceProvider services)
{
    public Task NotifyAsync(User user, CancellationToken cancellationToken) =>
        services.GetService<IRealtimePublisher>()?.PublishAsync(user.TenantId, UserEvents.Topic, UserEvents.Changed, new { userId = user.Id }, cancellationToken)
        ?? Task.CompletedTask;

    public static string Name(string? firstName, string? lastName, string? email) =>
        string.Join(' ', new[] { firstName, lastName }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name : email ?? string.Empty;
}
