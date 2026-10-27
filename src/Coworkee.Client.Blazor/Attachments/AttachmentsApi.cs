using System.Net.Http.Headers;
using System.Net.Http.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Attachments;

namespace Coworkee.Client.Blazor.Attachments;

internal sealed class AttachmentsApi(HttpClient http) : ApiClientBase(http), IAttachmentsApi
{
    public async Task<IReadOnlyList<AttachmentSlotDto>> GetAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default) =>
        await GetAsync<AttachmentSlotDto[]>($"{AttachmentUrls.Root}/{Uri.EscapeDataString(entityType)}/{entityId}", cancellationToken);

    public Task<AttachmentSlotDto> GetSlotAsync(string entityType, Guid entityId, string slot, CancellationToken cancellationToken = default) =>
        GetAsync<AttachmentSlotDto>(AttachmentUrls.Slot(entityType, entityId, slot), cancellationToken);

    public async Task<RecordAttachmentDto> UploadAsync(string entityType, Guid entityId, string slot, string fileName, string? contentType, Stream content, CancellationToken cancellationToken = default)
    {
        using var body = new StreamContent(content);
        body.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var type) ? type : new MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendContentAsync(HttpMethod.Post, $"{AttachmentUrls.Slot(entityType, entityId, slot)}?name={Uri.EscapeDataString(fileName)}", body, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<RecordAttachmentDto>(cancellationToken))!;
    }

    public Task DeleteAsync(RecordAttachmentDto attachment, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, AttachmentUrls.Content(attachment), null, cancellationToken);

    public Task<Contracts.Files.FileTokenDto> CreateTokenAsync(RecordAttachmentDto attachment, CancellationToken cancellationToken = default) =>
        SendAsync<Contracts.Files.FileTokenDto>(HttpMethod.Post, $"{AttachmentUrls.Slot(attachment.EntityType, attachment.EntityId, attachment.Slot)}/{attachment.Id}/token", null, cancellationToken);
}
