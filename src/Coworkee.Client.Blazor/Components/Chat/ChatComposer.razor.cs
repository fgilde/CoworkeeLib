using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudExRichTextEditor.Extensibility;
using MudExRichTextEditor.Types;

namespace Coworkee.Client.Blazor.Components.Chat;

/// <summary>
/// Writes a message: formatting, <c>@</c> people and <c>#</c> records, files from the button, drag and drop or pasted images with progress;
/// Enter sends, Shift+Enter breaks the line. Also edits an own message.
/// </summary>
public partial class ChatComposer : IAsyncDisposable
{
    private static readonly TimeSpan TypingEvery = TimeSpan.FromSeconds(3);
    private static readonly QuillTool[] ChatTools =
        [QuillTools.Bold(1), QuillTools.Italic(1), QuillTools.Strike(1), QuillTools.CodeBlock(2), QuillTools.Blockquote(2), QuillTools.BulletList(3), QuillTools.OrderedList(3), QuillTools.Link(4)];

    private readonly List<Upload> _uploads = [];
    private object[] _modules = [];
    private RichTextEditor? _editor;
    private ElementReference _host;
    private ElementReference _input;
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;
    private DotNetObjectReference<ChatComposer>? _self;
    private string _html = string.Empty;
    private DateTimeOffset _typedAt;
    private Guid _attachedTo;
    private ChatMessageDto? _editing;
    private bool _busy;

    [Inject] private IChatApi Api { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IServiceProvider Services { get; set; } = null!;

    [Parameter, EditorRequired] public Guid ConversationId { get; set; }

    [Parameter] public ChatMessageDto? ReplyTo { get; set; }

    [Parameter] public EventCallback<ChatMessageDto?> ReplyToChanged { get; set; }

    /// <summary>An own message to change; the composer shows its text.</summary>
    [Parameter] public ChatMessageDto? Editing { get; set; }

    [Parameter] public EventCallback<ChatMessageDto?> EditingChanged { get; set; }

    /// <summary>A message was sent or changed.</summary>
    [Parameter] public EventCallback<ChatMessageDto> Sent { get; set; }

    private static QuillTool[] Tools => ChatTools;

    private long MaxBytes =>
        (int.TryParse(Services.GetService<ThemeService>()?.ClientSettings.GetValueOrDefault(ChatSettings.MaxAttachmentMb), CultureInfo.InvariantCulture, out var mb) && mb > 0 ? mb : 25)
        * 1024L * 1024L;

    private string UploadUrl => $"api/v1/chat/chats/{ConversationId}/attachments";

    protected override void OnInitialized()
    {
        var mentions = new ChatMentionSource(Api, Services.GetService<Search.IGlobalSearchApi>());
        _modules = [new MentionModule<ChatMentionItem>(mentions.SearchAsync, '@', '#')];
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!ReferenceEquals(Editing, _editing))
        {
            _editing = Editing;
            await SetTextAsync(Editing?.Source ?? string.Empty);
        }

        if (_handle is not null && _attachedTo != ConversationId)
        {
            _attachedTo = ConversationId;
            _uploads.Clear();
            await _handle.InvokeVoidAsync("configure", UploadUrl, MaxBytes);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            _self = DotNetObjectReference.Create(this);
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/chat.js");
            _handle = await _module.InvokeAsync<IJSObjectReference>("attach", _host, _input, _self, new { url = UploadUrl, maxBytes = MaxBytes });
            _attachedTo = ConversationId;
        }
        catch (JSException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_handle is not null)
            {
                await _handle.InvokeVoidAsync("dispose");
                await _handle.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }

        _self?.Dispose();
    }

    [JSInvokable]
    public Task SendFromEnter() => InvokeAsync(SendAsync);

    [JSInvokable]
    public Task UploadStarted(string id, string name, long size) => InvokeAsync(() =>
    {
        _uploads.Add(new Upload(id, name, size));
        StateHasChanged();
    });

    [JSInvokable]
    public Task UploadProgress(string id, long loaded, long total) => InvokeAsync(() =>
    {
        if (Find(id) is { } upload)
        {
            upload.Percent = total > 0 ? 100.0 * loaded / total : 0;
            StateHasChanged();
        }
    });

    [JSInvokable]
    public Task UploadDone(string id, string json) => InvokeAsync(() =>
    {
        if (Find(id) is { } upload)
        {
            upload.Attachment = JsonSerializer.Deserialize<ChatAttachmentDto>(json, JsonSerializerOptions.Web);
            StateHasChanged();
        }
    });

    [JSInvokable]
    public Task UploadFailed(string id, int status) => InvokeAsync(() =>
    {
        if (Find(id) is { } upload)
        {
            upload.Failed = true;
            Snackbar.Add(status == 413 ? L["{0} is larger than {1} MB.", upload.Name, MaxBytes / 1024 / 1024] : L["{0} could not be uploaded.", upload.Name], Severity.Warning);
            StateHasChanged();
        }
    });

    /// <summary>Adds a finished upload, e.g. from a test or an own picker.</summary>
    public void AddAttachment(ChatAttachmentDto attachment) => _uploads.Add(new Upload(attachment.Id.ToString(), attachment.Name, attachment.Size) { Attachment = attachment });

    private Upload? Find(string id) => _uploads.FirstOrDefault(u => u.Id == id);

    private void Remove(Upload upload) => _uploads.Remove(upload);

    private async Task PickAsync()
    {
        if (_handle is not null)
        {
            await _handle.InvokeVoidAsync("pick");
        }
    }

    private async Task ChangedAsync(string html)
    {
        _html = html;
        var now = DateTimeOffset.UtcNow;
        if (Editing is null && HasText(html) && now - _typedAt > TypingEvery)
        {
            _typedAt = now;
            try
            {
                await Api.TypingAsync(ConversationId);
            }
            catch (Exception exception) when (exception is ApiException or HttpRequestException)
            {
            }
        }
    }

    private async Task SendAsync()
    {
        if (_busy || _uploads.Any(u => u.Attachment is null && !u.Failed))
        {
            return;
        }

        var html = _editor?.Editor is { } editor ? await editor.GetHtml() : _html;
        var attachments = _uploads.Where(u => u.Attachment is not null).Select(u => u.Attachment!.Id).ToList();
        if (!HasText(html) && (attachments.Count == 0 || Editing is not null))
        {
            return;
        }

        _busy = true;
        ChatMessageDto? sent = null;
        await Snackbar.RunAsync(async () => sent = Editing is { } editing
            ? await Api.EditAsync(editing.Id, html)
            : await Api.PostAsync(ConversationId, new PostChatMessageRequest(HasText(html) ? html : string.Empty, ReplyTo?.Id, attachments)));
        _busy = false;
        if (sent is null)
        {
            return;
        }

        _uploads.Clear();
        await SetTextAsync(string.Empty);
        if (Editing is not null)
        {
            _editing = null;
            await EditingChanged.InvokeAsync(null);
        }
        else if (ReplyTo is not null)
        {
            await ReplyToChanged.InvokeAsync(null);
        }

        await Sent.InvokeAsync(sent);
    }

    private async Task CancelEditAsync()
    {
        _editing = null;
        await SetTextAsync(string.Empty);
        await EditingChanged.InvokeAsync(null);
    }

    private async Task SetTextAsync(string html)
    {
        _html = html;
        if (_editor?.Editor is { } editor)
        {
            await editor.SetHtml(html);
        }
    }

    // mentions count as text, empty paragraphs and line breaks do not
    private static bool HasText(string? html) =>
        !string.IsNullOrEmpty(html) && (html.Contains("class=\"mention\"", StringComparison.Ordinal) || Tags().Replace(html, string.Empty).Replace("&nbsp;", " ").Trim().Length > 0);

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    private sealed class Upload(string id, string name, long size)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public long Size { get; } = size;

        public double Percent { get; set; }

        public ChatAttachmentDto? Attachment { get; set; }

        public bool Failed { get; set; }
    }
}
