using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<SubscriptionFrequency>))]
public enum SubscriptionFrequency
{
    Daily,
    Weekly,
    Monthly,
}
