namespace Coworkee.Contracts.Features;

public static class FeatureTopics
{
    /// <summary>Raised whenever an edition or the features of a tenant change; clients reload their features.</summary>
    public const string Changed = "global:features";
}
