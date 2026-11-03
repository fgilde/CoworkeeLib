namespace Coworkee.Contracts.Identity;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SetupCheckStatus>))]
public enum SetupCheckStatus
{
    Ok,
    Warning,
    Error,
}
