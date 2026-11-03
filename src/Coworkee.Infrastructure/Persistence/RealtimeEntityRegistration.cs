namespace Coworkee.Infrastructure.Persistence;

/// <summary>
/// An entity whose changes reach the clients like those of a [Realtime] entity although it carries no attribute, e.g. rows another system writes that
/// Coworkee.Realtime watches (AddRealtimeWatch): its topics need <see cref="Permission"/>, and client entity copies follow them.
/// </summary>
public sealed record RealtimeEntityRegistration(Type EntityType, string Permission);
