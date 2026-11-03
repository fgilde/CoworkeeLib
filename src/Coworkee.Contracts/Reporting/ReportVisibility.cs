using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

/// <summary>Who sees a report besides its owner.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReportVisibility>))]
public enum ReportVisibility
{
    Private,
    Roles,
    Organisation,

    /// <summary>The members of its groups and of the groups above them (<c>IGroupScope.ActiveAsync</c>: a group selection narrows it).</summary>
    Groups,
}
