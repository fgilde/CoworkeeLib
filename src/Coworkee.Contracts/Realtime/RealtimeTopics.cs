namespace Coworkee.Contracts.Realtime;

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
