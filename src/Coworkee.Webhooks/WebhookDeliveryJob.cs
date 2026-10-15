using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Webhooks;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Webhooks;

internal sealed class WebhookDeliveryQueuedHandler(IBackgroundJobs jobs) : IDomainEventHandler<WebhookDeliveryQueued>
{
    public Task HandleAsync(WebhookDeliveryQueued domainEvent, CancellationToken cancellationToken)
    {
        jobs.Enqueue<WebhookDeliveryJob, Guid>(domainEvent.DeliveryId);
        return Task.CompletedTask;
    }
}

/// <summary>One attempt of a delivery: sends, logs, schedules the next attempt with exponential backoff and switches failing endpoints off.</summary>
internal sealed class WebhookDeliveryJob(
    CoworkeeDbContext db, IHttpClientFactory http, WebhookSecrets secrets, IBackgroundJobs jobs, WebhookAdmins admins, TimeProvider clock, IOptions<WebhookOptions> options)
    : IBackgroundJob<Guid>
{
    public const string HttpClientName = "Coworkee.Webhooks";

    private WebhookOptions Options => options.Value;

    public async Task ExecuteAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        if (await db.Set<WebhookDelivery>().FirstOrDefaultAsync(d => d.Id == deliveryId && d.Status == WebhookDeliveryStatus.Pending, cancellationToken) is not { } delivery)
        {
            return;
        }

        var subscription = await db.Set<WebhookSubscription>().FirstOrDefaultAsync(s => s.Id == delivery.SubscriptionId, cancellationToken);
        if (subscription is null || (!subscription.IsActive && delivery.EventType != WebhookEventTypes.Test))
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            delivery.Error = subscription is null ? "The endpoint was deleted." : "The endpoint is switched off.";
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = clock.GetUtcNow();
        delivery.Attempts++;
        delivery.LastAttemptAt = now;
        delivery.NextAttemptAt = null;
        if (await SendAsync(subscription, delivery, now, cancellationToken))
        {
            delivery.Status = WebhookDeliveryStatus.Succeeded;
            subscription.ConsecutiveFailures = 0;
        }
        else if (delivery.Attempts < Options.MaxAttempts)
        {
            var delay = WebhookBackoff.Delay(delivery.Attempts, Options);
            delivery.NextAttemptAt = now + delay;
            await db.SaveChangesAsync(cancellationToken);
            jobs.Schedule<WebhookDeliveryJob, Guid>(delivery.Id, delay);
            return;
        }
        else
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            await CountFailureAsync(subscription, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task CountFailureAsync(WebhookSubscription subscription, DateTimeOffset now, CancellationToken cancellationToken)
    {
        subscription.ConsecutiveFailures++;
        if (subscription.IsActive && subscription.ConsecutiveFailures >= Options.DisableAfterFailures)
        {
            subscription.IsActive = false;
            subscription.DisabledAt = now;
            await admins.NotifyDisabledAsync(subscription, cancellationToken);
        }
    }

    private async Task<bool> SendAsync(WebhookSubscription subscription, WebhookDelivery delivery, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = delivery.EventId.ToString();
        var timestamp = now.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url) { Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json") };
        request.Headers.Add(WebhookSignature.IdHeader, id);
        request.Headers.Add(WebhookSignature.TimestampHeader, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add(WebhookSignature.SignatureHeader, WebhookSignature.Sign(secrets.Unprotect(subscription.ProtectedSecret), id, timestamp, delivery.Payload));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Coworkee-Webhooks", "1"));
        delivery.Request = Log($"POST {subscription.Url}", request.Headers, delivery.Payload);
        delivery.Response = null;
        delivery.ResponseStatus = null;
        delivery.Error = null;

        var watch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Options.Timeout);
            using var response = await http.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            delivery.ResponseStatus = (int)response.StatusCode;
            delivery.Response = Log($"HTTP {(int)response.StatusCode}", response.Headers, await ReadAsync(response, timeout.Token));
            if (!response.IsSuccessStatusCode)
            {
                delivery.Error = $"The endpoint answered {(int)response.StatusCode}.";
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or WebhookBlockedException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            delivery.Error = exception is OperationCanceledException ? "No answer within the timeout." : Truncate((exception.InnerException as WebhookBlockedException ?? exception).Message, 2000);
            return false;
        }
        finally
        {
            delivery.DurationMs = watch.ElapsedMilliseconds;
        }
    }

    private async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        var buffer = new char[Options.LogBodyLimit + 1];
        var read = await reader.ReadBlockAsync(buffer, cancellationToken);
        return Truncate(new string(buffer, 0, read), Options.LogBodyLimit);
    }

    // the signature is the only secret in the request; it is masked so the log cannot be replayed
    private string Log(string firstLine, HttpHeaders headers, string body)
    {
        var lines = headers.Select(h => $"{h.Key}: {(h.Key == WebhookSignature.SignatureHeader ? "v1,***" : string.Join(", ", h.Value))}");
        return Truncate(string.Join('\n', [firstLine, .. lines, string.Empty, body]), Options.LogBodyLimit + 1000);
    }

    private static string Truncate(string text, int limit) => text.Length <= limit ? text : text[..limit] + "…";
}

public static class WebhookBackoff
{
    /// <summary>The wait after failed attempt <paramref name="attempt"/> (1 based): the first delay, doubled per attempt, capped.</summary>
    public static TimeSpan Delay(int attempt, WebhookOptions options) =>
        TimeSpan.FromTicks((long)Math.Min(options.MaxRetryDelay.Ticks, options.FirstRetryDelay.Ticks * Math.Pow(2, attempt - 1)));
}

internal sealed class WebhookSecrets(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Coworkee.Webhooks");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
