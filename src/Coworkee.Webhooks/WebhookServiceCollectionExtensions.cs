using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Webhooks;

public static class WebhookServiceCollectionExtensions
{
    public static IServiceCollection AddCoworkeeWebhooks(this IServiceCollection services, Action<WebhookOptions> configure) => services.Configure(configure);
}
