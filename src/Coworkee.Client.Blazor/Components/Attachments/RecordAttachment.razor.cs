using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Attachments;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Files;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Attachments;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components;

namespace Coworkee.Client.Blazor.Components.Attachments;

/// <summary>
/// The files of one slot of a record registered with AddRecordAttachments: opens them in the file viewer, downloads, and for users who may
/// change the slot uploads (replacing the file of a single-file slot) and deletes. Changes by others appear live.
/// </summary>
public partial class RecordAttachment : IAsyncDisposable
{
    private AttachmentSlotDto? _slot;
    private (string, Guid, string)? _loaded;
    private IAsyncDisposable? _subscription;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private IAttachmentsApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid EntityId { get; set; }

    [Parameter, EditorRequired] public string Slot { get; set; } = string.Empty;

    [Parameter] public string? Label { get; set; }

    /// <summary>Hides upload and delete even for users who may change the slot.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<RecordAttachmentDto>> Changed { get; set; }

    private bool CanWrite => _slot is { CanWrite: true } && !ReadOnly;

    private string[] MimeTypes => [.. _slot?.ContentTypes ?? []];

    protected override async Task OnParametersSetAsync()
    {
        if (_loaded != (EntityType, EntityId, Slot))
        {
            _loaded = (EntityType, EntityId, Slot);
            await UnsubscribeAsync();
            _subscription = await Realtime.SubscribeAsync(AttachmentTopics.Record(EntityType, EntityId), _ => ReloadAsync());
            await LoadAsync();
        }
    }

    public async ValueTask DisposeAsync() => await UnsubscribeAsync();

    private async Task UnsubscribeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
            _subscription = null;
        }
    }

    private Task<bool> LoadAsync() => Snackbar.RunAsync(async () => _slot = await Api.GetSlotAsync(EntityType, EntityId, Slot));

    private async Task UploadAsync(UploadableFile? file)
    {
        if (file?.Data is not { Length: > 0 } data)
        {
            return;
        }

        if (await Snackbar.RunAsync(() => Api.UploadAsync(EntityType, EntityId, Slot, file.FileName, file.ContentType, new MemoryStream(data))))
        {
            await ReloadAsync();
        }
    }

    private async Task DeleteAsync(RecordAttachmentDto item)
    {
        if (await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? This cannot be undone.", item.FileName], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever)
            && await Snackbar.RunAsync(() => Api.DeleteAsync(item)))
        {
            await ReloadAsync();
        }
    }

    private Task PreviewAsync(RecordAttachmentDto item) => FilePreviewDialog.ShowAsync(Dialogs, AttachmentUrls.Content(item), item.ContentType, item.FileName);

    // the upload field raises its event outside the render cycle of this component
    private async Task ReloadAsync()
    {
        if (await LoadAsync())
        {
            await InvokeAsync(StateHasChanged);
            await Changed.InvokeAsync(_slot!.Items);
        }
    }

    private static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };
}
