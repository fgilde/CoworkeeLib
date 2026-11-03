using Coworkee.Contracts.Mailing;

namespace Coworkee.Mailing;

public sealed class MailOptions
{
    public const string Section = "Coworkee:Mail";

    /// <summary>A mail in <see cref="OutgoingMailStatus.Sending"/> for longer than this was left by a sender that stopped (a crash, a restart).</summary>
    public TimeSpan SendingTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// False (default): such a mail fails with <see cref="StuckMailSweep.Message"/>, since the provider may have sent it. True: it is queued again,
    /// which sends it twice when the provider had sent it.
    /// </summary>
    public bool RetryStuckSending { get; set; }
}
