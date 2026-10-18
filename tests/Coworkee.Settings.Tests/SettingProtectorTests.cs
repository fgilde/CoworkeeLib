using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Settings.Tests;

public sealed class SettingProtectorTests
{
    [Fact]
    public void A_secret_of_another_key_ring_reads_as_unset()
    {
        var written = new SettingProtector(new EphemeralDataProtectionProvider(), NullLogger<SettingProtector>.Instance).Protect("sk-test");
        var current = new SettingProtector(new EphemeralDataProtectionProvider(), NullLogger<SettingProtector>.Instance);

        current.Unprotect(written).ShouldBeNull();
    }

    [Fact]
    public void A_secret_of_the_same_key_ring_reads_back()
    {
        var protector = new SettingProtector(new EphemeralDataProtectionProvider(), NullLogger<SettingProtector>.Instance);

        protector.Unprotect(protector.Protect("sk-test")).ShouldBe("sk-test");
    }
}
