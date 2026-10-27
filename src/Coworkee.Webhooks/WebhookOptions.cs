using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Webhooks;

/// <summary>Delivery settings from "Coworkee:Webhooks" and the events an app offers, added in code with AddCoworkeeWebhooks.</summary>
public sealed class WebhookOptions
{
    public const string Section = "Coworkee:Webhooks";

    /// <summary>Endpoints must use https; switch off only for local tests.</summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>Lets endpoints reach loopback, private and link-local addresses; for tests and closed networks only.</summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>Ports besides 443 (and 80 without <see cref="RequireHttps"/>) an endpoint may use.</summary>
    public int[] AllowedPorts { get; set; } = [];

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Attempts per delivery, the first one included.</summary>
    public int MaxAttempts { get; set; } = 6;

    public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(6);

    /// <summary>An endpoint whose deliveries failed this many times in a row is switched off and the admins are told.</summary>
    public int DisableAfterFailures { get; set; } = 5;

    /// <summary>Request and response bodies in the delivery log are cut after this many characters.</summary>
    public int LogBodyLimit { get; set; } = 4096;

    /// <summary>The default days of the delivery log; with the Retention module the setting Retention.webhook-deliveries.Days decides.</summary>
    public int LogRetentionDays { get; set; } = 30;

    /// <summary>How long the old secret still signs deliveries after a rotation, next to the new one (Standard Webhooks allows several signatures).</summary>
    public TimeSpan SecretGracePeriod { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Domain events (by type name) other modules raise that become webhook events, with their data as they are; workflow transitions by default.</summary>
    public Dictionary<string, string> DomainEvents { get; set; } = new(StringComparer.Ordinal) { ["WorkflowTransitioned"] = "workflow.transitioned" };

    internal HashSet<Type> Entities { get; } = [];

    internal Dictionary<string, string?> AppEvents { get; } = new(StringComparer.Ordinal);

    /// <summary>Changes of <typeparamref name="TEntity"/> (a registered OData set) become events like "products.created", "products.updated" and "products.deleted".</summary>
    public WebhookOptions Entity<TEntity>()
        where TEntity : class
    {
        Coworkee.OData.ODataEntityAccess.RequireGuidId<TEntity>("Webhook entity events");
        Entities.Add(typeof(TEntity));
        return this;
    }

    /// <summary>An event the app publishes with <see cref="IWebhookPublisher"/>; listed for the admins to pick.</summary>
    public WebhookOptions Event(string type, string? description = null)
    {
        AppEvents[type] = description;
        return this;
    }
}

public static class WebhookServiceCollectionExtensions
{
    public static IServiceCollection AddCoworkeeWebhooks(this IServiceCollection services, Action<WebhookOptions> configure) => services.Configure(configure);
}
