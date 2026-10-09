using System.Text.Json;

namespace Coworkee.Contracts.Realtime;

public sealed record RealtimeEnvelope(string Topic, string Type, JsonElement Payload);

/// <summary>Why the session ended: "signed-out", "locked" or "password-changed" (in another session or by an administrator).</summary>
public sealed record SessionRevokedPayload(string Reason);

public sealed record EntityChangedPayload(string EntityType, string EntityId, string Action, IReadOnlyList<string> ChangedProperties);

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
}

public static class RealtimeTopics
{
    public static string Type(string entityType) => $"type:{entityType}";

    public static string Entity(string entityType, object id) => $"entity:{entityType}:{id}";

    public static string User(Guid userId) => $"user:{userId}";
}
