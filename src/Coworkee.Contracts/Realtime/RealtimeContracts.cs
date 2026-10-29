using System.Text.Json;

namespace Coworkee.Contracts.Realtime;

public sealed record RealtimeEnvelope(string Topic, string Type, JsonElement Payload);

/// <summary>Why the session ended: "signed-out", "locked" or "password-changed" (in another session or by an administrator).</summary>
public sealed record SessionRevokedPayload(string Reason);

/// <summary>
/// "Log in as" <paramref name="TargetId"/> by <paramref name="ImpersonatorId"/> ended: stopped, expired or revoked at the auth server. Sent only on the
/// impersonating session's topic (<see cref="RealtimeTopics.Session"/>), never to the target's own sessions.
/// </summary>
public sealed record ImpersonationEndedPayload(Guid TargetId, Guid ImpersonatorId);

public sealed record EntityChangedPayload(string EntityType, string EntityId, string Action, IReadOnlyList<string> ChangedProperties)
{
    public const string Created = "Created";
    public const string Updated = "Updated";

    /// <summary>Removed, also by a soft delete.</summary>
    public const string Deleted = "Deleted";

    public bool IsDeleted => Action == Deleted;

    public static EntityChangedPayload? From(RealtimeEnvelope envelope) =>
        envelope.Type == RealtimeEventTypes.EntityChanged ? envelope.Payload.Deserialize<EntityChangedPayload>(JsonSerializerOptions.Web) : null;
}

public static class RealtimeHubMethods
{
    public const string Path = "/hubs/realtime";
    public const string OnEvent = "OnEvent";
    public const string Subscribe = "Subscribe";
    public const string Unsubscribe = "Unsubscribe";
}

public static class RealtimeEventTypes
{
    public const string EntityChanged = "EntityChanged";

    /// <summary>On the user's own topic: the session ended (signed out or locked by an administrator, new password); the client ends it too.</summary>
    public const string SessionRevoked = "SessionRevoked";

    /// <summary>On the impersonating session's topic (<see cref="RealtimeTopics.Session"/>): the impersonation ended (<see cref="ImpersonationEndedPayload"/>); the client signs in again.</summary>
    public const string ImpersonationEnded = "ImpersonationEnded";
}

public static class RealtimeTopics
{
    public static string Type(string entityType) => $"type:{entityType}";

    public static string Entity(string entityType, object id) => $"entity:{entityType}:{id}";

    public static string User(Guid userId) => $"user:{userId}";

    /// <summary>One sign-in session (the <c>sid</c> of its tokens): only connections whose token carries that <c>sid</c> may follow it.</summary>
    public static string Session(string sessionId) => $"{SessionPrefix}{sessionId}";

    public const string SessionPrefix = "session:";

    /// <summary>Roles, grants, memberships or the active role changed in the organisation: every client reloads its permissions.</summary>
    public const string TenantPermissions = "tenant:permissions";

    /// <summary>A change that touches the permissions of every organisation, e.g. switching the active role setting.</summary>
    public const string GlobalPermissions = "global:permissions";
}

/// <summary>Who of the organisation is online: <see cref="Changed"/> with a <see cref="PresenceDto"/> on <see cref="Topic"/>.</summary>
public static class PresenceEvents
{
    public const string Topic = "tenant:presence";
    public const string Changed = "presence.changed";
    public const string Path = "/api/v1/realtime/presence";
}

public sealed record PresenceDto(Guid UserId, bool Online);
