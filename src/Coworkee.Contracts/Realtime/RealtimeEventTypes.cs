namespace Coworkee.Contracts.Realtime;

public static class RealtimeEventTypes
{
    public const string EntityChanged = "EntityChanged";

    /// <summary>On the user's own topic: the session ended (signed out or locked by an administrator, new password); the client ends it too.</summary>
    public const string SessionRevoked = "SessionRevoked";

    /// <summary>On the impersonating session's topic (<see cref="RealtimeTopics.Session"/>): the impersonation ended (<see cref="ImpersonationEndedPayload"/>); the client signs in again.</summary>
    public const string ImpersonationEnded = "ImpersonationEnded";
}
