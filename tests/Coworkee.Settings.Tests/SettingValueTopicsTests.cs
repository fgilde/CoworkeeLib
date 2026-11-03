using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Coworkee.Domain;

namespace Coworkee.Settings.Tests;

/// <summary>A saved setting tells the clients it reaches, so open pages follow it at once (the assistant switched on or off, a table's page mode).</summary>
public sealed class SettingValueTopicsTests
{
    private static readonly IRealtimeTopicMapper Topics = new SettingValueTopics();

    [Fact]
    public void A_global_setting_reaches_every_organisation() =>
        Topics.TopicsFor(new SettingValue { Name = "Ai.Enabled", Scope = SettingScope.Global }).ShouldBe([SettingTopics.Global]);

    [Fact]
    public void An_organisation_setting_reaches_the_organisation() =>
        Topics.TopicsFor(new SettingValue { Name = "Ai.Enabled", Scope = SettingScope.Tenant, ScopeKey = Guid.NewGuid() }).ShouldBe([SettingTopics.Tenant]);

    [Fact]
    public void A_user_setting_reaches_the_user()
    {
        var user = Guid.NewGuid();

        Topics.TopicsFor(new SettingValue { Name = "Theme.Mode", Scope = SettingScope.User, ScopeKey = user }).ShouldBe([RealtimeTopics.User(user)]);
    }

    [Fact]
    public void Other_rows_have_no_setting_topics() => Topics.TopicsFor(new object()).ShouldBeEmpty();

    [Fact]
    public void The_settings_module_registers_the_topics()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        Core.Modularity.ModuleServiceCollectionExtensions.AddCoworkeeModules<CoworkeeSettingsModule>(services, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        services.ShouldContain(d => d.ServiceType == typeof(IRealtimeTopicMapper) && d.ImplementationType == typeof(SettingValueTopics));
    }
}
