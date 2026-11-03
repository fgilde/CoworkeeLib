using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

[JsonConverter(typeof(JsonStringEnumConverter<DataTransferStatus>))]
public enum DataTransferStatus
{
    Draft,
    Queued,
    Running,
    Completed,
    Failed,
    Undone,

    /// <summary>An export the user stopped (<c>POST /api/v1/data/jobs/{id}/cancel</c>): it frees its slot, writes no file and sends no mail.</summary>
    Cancelled,
}
