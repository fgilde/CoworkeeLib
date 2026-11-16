using Coworkee.Contracts.Data;
using Coworkee.Domain;
using Microsoft.Extensions.DependencyInjection;
using Nextended.Core.Attributes;

namespace Coworkee.Settings;

/// <summary>
/// A saved client entity override (Coworkee:ClientEntities:...) tells every open client, which loads the manifest again; the event carries the key, never
/// the value. The section itself is registered in <see cref="CoworkeeSettingsModule"/>, so the Local copies page saves through SaveAppConfiguration.
/// </summary>
[RegisterAs(typeof(IRealtimeTopicMapper), ServiceLifetime = ServiceLifetime.Singleton)]
internal sealed class ClientEntityConfigurationTopics : IRealtimeTopicMapper
{
    private const string Prefix = "Coworkee:ClientEntities:";

    public IEnumerable<string> TopicsFor(object entity) =>
        entity is ConfigurationEntry { Key: { } key } && key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? [ClientEntityTopics.Changed] : [];
}
