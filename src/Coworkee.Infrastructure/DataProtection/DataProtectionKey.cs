using Coworkee.Domain;

namespace Coworkee.Infrastructure.DataProtection;

[NotAudited]
public sealed class DataProtectionKey
{
    public int Id { get; set; }

    public string? FriendlyName { get; set; }

    public required string Xml { get; set; }
}
