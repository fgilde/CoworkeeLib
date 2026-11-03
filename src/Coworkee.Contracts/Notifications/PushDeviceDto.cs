namespace Coworkee.Contracts.Notifications;

public sealed record PushDeviceDto(Guid Id, string Endpoint, string? Device, DateTimeOffset CreatedAt);
