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
