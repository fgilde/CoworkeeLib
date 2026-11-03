namespace Coworkee.Contracts.Notifications;

/// <summary>The VAPID public key browsers subscribe with (null: push is not configured) and the user's devices that receive pushes.</summary>
public sealed record PushSettingsDto(string? PublicKey, IReadOnlyList<PushDeviceDto> Devices);
