using System.Buffers.Text;
using System.Net;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Notifications.Push;

[AiTool(Exclude = true)]
public sealed record GetPushSettings : IQuery<Result<PushSettingsDto>>;

/// <summary>Turns push on for the browser that sends it; the same browser signed in as someone else moves to that account.</summary>
[AiTool(Exclude = true)]
public sealed record SavePushSubscription(PushSubscriptionRequest Subscription) : ICommand<Result<PushDeviceDto>>;

[AiTool(Exclude = true)]
public sealed record DeletePushSubscription(Guid Id) : ICommand<Result>;

internal sealed class SavePushSubscriptionValidator : AbstractValidator<SavePushSubscription>
{
    public SavePushSubscriptionValidator()
    {
        // the server posts to this address: only a push service on the internet, never a host of the own network
        RuleFor(c => c.Subscription.Endpoint).NotEmpty().MaximumLength(2000).Must(IsPushService).WithMessage("Not the address of a push service.");
        RuleFor(c => c.Subscription.P256dh).Must(k => Decodes(k, 65)).WithMessage("Not a P-256 public key.");
        RuleFor(c => c.Subscription.Auth).Must(k => Decodes(k, 16)).WithMessage("Not a 16 byte secret.");
        RuleFor(c => c.Subscription.Device).MaximumLength(200);
    }

    internal static bool IsPushService(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && uri.HostNameType == UriHostNameType.Dns && !IPAddress.TryParse(uri.IdnHost, out _) && uri.IdnHost.Contains('.')
        && !uri.IdnHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && !uri.IdnHost.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
        && !uri.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    private static bool Decodes(string? value, int length)
    {
        try
        {
            return value is { Length: > 0 and < 200 } && Base64Url.DecodeFromChars(value).Length == length;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal sealed class PushSubscriptionHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IOptions<NotificationOptions> options, TimeProvider clock)
    : IHandler<GetPushSettings, Result<PushSettingsDto>>,
      IHandler<SavePushSubscription, Result<PushDeviceDto>>,
      IHandler<DeletePushSubscription, Result>
{
    public async Task<Result<PushSettingsDto>> HandleAsync(GetPushSettings query, CancellationToken cancellationToken)
    {
        var push = options.Value.Push;
        var devices = await Mine().AsNoTracking().OrderBy(s => s.CreatedAt).Select(s => new PushDeviceDto(s.Id, s.Endpoint, s.Device, s.CreatedAt)).ToListAsync(cancellationToken);
        return new PushSettingsDto(PushConfiguration.IsOn(push) ? push.PublicKey : null, devices);
    }

    public async Task<Result<PushDeviceDto>> HandleAsync(SavePushSubscription command, CancellationToken cancellationToken)
    {
        if (!PushConfiguration.IsOn(options.Value.Push))
        {
            return Error.Validation("push", "Push notifications are not configured.");
        }

        var request = command.Subscription;
        var device = await db.Set<PushSubscription>().SingleOrDefaultAsync(s => s.Endpoint == request.Endpoint, cancellationToken);
        if (device is null)
        {
            device = new PushSubscription { Endpoint = request.Endpoint, P256dh = request.P256dh, Auth = request.Auth, CreatedAt = clock.GetUtcNow() };
            db.Add(device);
        }

        device.UserId = currentUser.UserId!.Value;
        device.P256dh = request.P256dh;
        device.Auth = request.Auth;
        device.Device = request.Device;
        return new PushDeviceDto(device.Id, device.Endpoint, device.Device, device.CreatedAt);
    }

    public async Task<Result> HandleAsync(DeletePushSubscription command, CancellationToken cancellationToken)
    {
        if (await Mine().SingleOrDefaultAsync(s => s.Id == command.Id, cancellationToken) is not { } device)
        {
            return Error.NotFound("notifications.push_not_found", "The device does not receive push notifications.");
        }

        db.Remove(device);
        return Result.Success();
    }

    private IQueryable<PushSubscription> Mine()
    {
        var userId = currentUser.UserId;
        return db.Set<PushSubscription>().Where(s => s.UserId == userId);
    }
}

public static class PushConfiguration
{
    public static bool IsOn(PushOptions push) => !string.IsNullOrWhiteSpace(push.PublicKey) && !string.IsNullOrWhiteSpace(push.PrivateKey);
}
