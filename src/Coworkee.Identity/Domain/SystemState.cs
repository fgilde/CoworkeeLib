namespace Coworkee.Identity.Domain;

public sealed class SystemState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public bool IsInitialized { get; set; }

    public DateTimeOffset? InitializedAt { get; set; }
}
