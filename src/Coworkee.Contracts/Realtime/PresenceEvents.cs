namespace Coworkee.Contracts.Realtime;

/// <summary>Who of the organisation is online: <see cref="Changed"/> with a <see cref="PresenceDto"/> on <see cref="Topic"/>.</summary>
public static class PresenceEvents
{
    public const string Topic = "tenant:presence";
    public const string Changed = "presence.changed";
    public const string Path = "/api/v1/realtime/presence";
}
