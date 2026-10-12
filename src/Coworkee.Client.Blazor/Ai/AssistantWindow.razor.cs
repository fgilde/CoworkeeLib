using System.Text;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>
/// The assistant window: conversations on the left, the open one with streamed answers on the right. Conversations live on the server,
/// so other tabs follow along through the realtime hub.
/// </summary>
public partial class AssistantWindow : IAsyncDisposable
{
    private static readonly (AssistantSize Size, string Icon, string Title)[] Sizes =
    [
        (AssistantSize.Small, Icons.Material.Outlined.CropPortrait, "Small"),
        (AssistantSize.Large, Icons.Material.Outlined.CropLandscape, "Large"),
        (AssistantSize.Full, Icons.Material.Outlined.Fullscreen, "Full screen"),
    ];

    private readonly CancellationTokenSource _closed = new();
    private readonly List<AiMessageDto> _messages = [];
    private readonly List<AiToolCallDto> _streamCalls = [];
    private IReadOnlyList<AiSessionDto> _sessions = [];
    private AiSessionDto? _session;
    private StringBuilder? _streaming;
    private string? _input;
    private string? _search;
    private bool _busy;
    private bool _showSessions;
    private bool _scroll;
    private ElementReference _scroller;
    private ElementReference _textarea;
    private IJSObjectReference? _module;
    private DotNetObjectReference<AssistantWindow>? _self;
    private IAsyncDisposable? _subscription;

    [Inject] private IAssistantApi Api { get; set; } = null!;

    [Inject] private AssistantState State { get; set; } = null!;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    // the small window shows the list over the chat when asked for, the larger ones beside it unless hidden
    private bool SessionsVisible => State.Size == AssistantSize.Small ? _showSessions : !_showSessions;

    private AiMessageDto Streamed => new(Guid.Empty, "assistant", _streaming?.ToString() ?? string.Empty, [.. _streamCalls], [], null, DateTimeOffset.Now);

    protected override async Task OnInitializedAsync()
    {
        await LoadSessionsAsync();
        if (_sessions.Count > 0)
        {
            await SelectAsync(_sessions[0]);
        }

        var user = AuthenticationState is null ? null : (await AuthenticationState).User;
        if (Guid.TryParse(user?.FindFirst("sub")?.Value, out var me))
        {
            _subscription = await Realtime.SubscribeAsync(RealtimeTopics.User(me), envelope => InvokeAsync(() => ReceiveAsync(envelope)));
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _self = DotNetObjectReference.Create(this);
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            await _module.InvokeVoidAsync("submitOnEnter", _textarea, _self);
            await _module.InvokeVoidAsync("focus", _textarea);
        }

        if (_scroll && _module is not null)
        {
            _scroll = false;
            await _module.InvokeVoidAsync("scrollToEnd", _scroller);
        }
    }

    [JSInvokable]
    public Task SubmitFromEnter() => InvokeAsync(SendAsync);

    public async ValueTask DisposeAsync()
    {
        await _closed.CancelAsync();
        _closed.Dispose();
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }

        _self?.Dispose();
    }

    private void Key(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            State.Close();
        }
    }

    private async Task ReceiveAsync(RealtimeEnvelope envelope)
    {
        if (EntityChangedPayload.From(envelope) is not { EntityType: "AiChatSession" } change || _busy)
        {
            return;
        }

        await LoadSessionsAsync();
        if (_session?.Id.ToString() == change.EntityId)
        {
            if (change.IsDeleted)
            {
                NewSession();
            }
            else
            {
                await SelectAsync(_session);
            }
        }

        StateHasChanged();
    }

    private void NewSession()
    {
        _session = null;
        _messages.Clear();
        _input = null;
        _showSessions = State.Size != AssistantSize.Small && _showSessions;
    }

    private async Task SearchAsync(string? search)
    {
        _search = search;
        await LoadSessionsAsync();
    }

    private async Task LoadSessionsAsync()
    {
        await Snackbar.RunAsync(async () => _sessions = await Api.GetSessionsAsync(_search, _closed.Token));
    }

    private async Task SelectAsync(AiSessionDto session)
    {
        await Snackbar.RunAsync(async () =>
        {
            var detail = await Api.GetSessionAsync(session.Id, _closed.Token);
            _session = new AiSessionDto(detail.Id, detail.Title, detail.UpdatedAt);
            _messages.Clear();
            _messages.AddRange(detail.Messages);
            _scroll = true;
        });
        if (State.Size == AssistantSize.Small)
        {
            _showSessions = false;
        }
    }

    private async Task RenameAsync((AiSessionDto Session, string Title) rename)
    {
        if (await Snackbar.RunAsync(() => Api.RenameSessionAsync(rename.Session.Id, rename.Title, _closed.Token)))
        {
            _session = _session?.Id == rename.Session.Id ? _session with { Title = rename.Title } : _session;
            await LoadSessionsAsync();
        }
    }

    private async Task DeleteAsync(AiSessionDto session)
    {
        if (!await Dialogs.ConfirmAsync(L["Delete conversation"], L["Delete {0}? This cannot be undone.", session.Title], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever))
        {
            return;
        }

        if (await Snackbar.RunAsync(() => Api.DeleteSessionAsync(session.Id, _closed.Token)))
        {
            if (_session?.Id == session.Id)
            {
                NewSession();
            }

            await LoadSessionsAsync();
        }
    }

    private async Task SendAsync()
    {
        var text = _input?.Trim();
        if (string.IsNullOrEmpty(text) || _busy)
        {
            return;
        }

        _input = null;
        var question = new AiMessageDto(Guid.NewGuid(), "user", text, [], [], null, DateTimeOffset.Now);
        await StreamAsync(
            async () => (_session ??= await Api.CreateSessionAsync(_closed.Token)).Id,
            id => Api.SendAsync(id, text, _closed.Token),
            question,
            () => _input = text);
    }

    private Task DecideAsync(bool approve) =>
        _session is { } session ? StreamAsync(() => Task.FromResult(session.Id), id => Api.ConfirmAsync(id, approve, _closed.Token), null, () => { }) : Task.CompletedTask;

    /// <summary>Shows the answer as it streams in; on failure the question goes back into the input.</summary>
    private async Task StreamAsync(Func<Task<Guid>> session, Func<Guid, IAsyncEnumerable<AiStreamEvent>> stream, AiMessageDto? question, Action restore)
    {
        _busy = true;
        _streaming = new StringBuilder();
        _streamCalls.Clear();
        if (question is not null)
        {
            _messages.Add(question);
        }

        _scroll = true;
        StateHasChanged();
        string? failure = null;
        try
        {
            await foreach (var item in stream(await session()))
            {
                switch (item.Type)
                {
                    case AiStreamEvent.Delta:
                        _streaming.Append(item.Text);
                        break;
                    case AiStreamEvent.Tool when item.ToolCall is not null:
                        _streamCalls.Add(item.ToolCall);
                        break;
                    case AiStreamEvent.Done when item.Message is not null:
                        _messages.Add(item.Message);
                        break;
                    case AiStreamEvent.Error:
                        failure = item.Text;
                        break;
                }

                _scroll = true;
                StateHasChanged();
            }
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        {
            return;
        }
        catch (ApiException exception)
        {
            failure = SnackbarApiExtensions.Describe(exception);
        }
        catch (HttpRequestException)
        {
            failure = L["The assistant did not answer. Try again."];
        }
        finally
        {
            _busy = false;
            _streaming = null;
        }

        if (failure is not null)
        {
            if (question is not null)
            {
                _messages.Remove(question);
            }

            restore();
            Snackbar.Add(failure, Severity.Error);
            StateHasChanged();
        }

        await LoadSessionsAsync();
        if (failure is null && _session is { } current)
        {
            await SelectAsync(current);
        }

        // also when Enter sent it: calls from JavaScript do not render on their own
        StateHasChanged();
    }
}
