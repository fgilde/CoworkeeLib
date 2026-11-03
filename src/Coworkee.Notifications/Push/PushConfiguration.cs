using Coworkee.Contracts.Configuration;

namespace Coworkee.Notifications.Push;

public static class PushConfiguration
{
    public static bool IsOn(PushOptions push) => !string.IsNullOrWhiteSpace(push.PublicKey) && !string.IsNullOrWhiteSpace(push.PrivateKey);
}
