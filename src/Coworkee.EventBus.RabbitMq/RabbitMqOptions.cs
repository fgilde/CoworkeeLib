namespace Coworkee.EventBus.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string Section = "Coworkee:EventBus:RabbitMq";
    public const string ConnectionStringName = "rabbitmq";

    /// <summary>amqp:// url; falls back to the "rabbitmq" connection string.</summary>
    public string? ConnectionString { get; set; }

    public string Exchange { get; set; } = "coworkee.events";

    /// <summary>The queue of this app: instances of one app share it, every other app needs its own. Defaults to the entry assembly name.</summary>
    public string? Queue { get; set; }

    /// <summary>Deliveries of a message before it moves to the "{Queue}.dead" queue.</summary>
    public int MaxDeliveries { get; set; } = 5;

    public ushort Prefetch { get; set; } = 16;
}
