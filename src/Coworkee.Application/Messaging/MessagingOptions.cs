namespace Coworkee.Application.Messaging;

public sealed class MessagingOptions
{
    public const string Section = "Coworkee:Messaging";

    public TimeSpan SlowRequestThreshold { get; set; } = TimeSpan.FromMilliseconds(500);
}
