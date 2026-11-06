using Coworkee.Contracts.Settings;

namespace Coworkee.Theming.Tests;

public sealed class ThemeTopicsTests
{
    private static ThemeDefinition Theme(Guid? tenantId) => new() { Name = "T", TenantId = tenantId, PaletteLight = "{}", PaletteDark = "{}" };

    [Fact]
    public void Every_client_of_the_organisation_hears_of_a_changed_theme_or_default_not_only_the_theme_managers()
    {
        var topics = new ThemeTopics();

        topics.TopicsFor(Theme(Guid.NewGuid())).ShouldBe([SettingTopics.Tenant]);
        topics.TopicsFor(new TenantTheme()).ShouldBe([SettingTopics.Tenant]);
        topics.TopicsFor(Theme(null)).ShouldBe([SettingTopics.Global]);
    }
}
