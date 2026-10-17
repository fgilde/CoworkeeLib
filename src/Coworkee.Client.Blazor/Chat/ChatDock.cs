using System.Text.Json;
using Coworkee.Client.Blazor.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Chat;

/// <summary>An open mini chat: a window of the dock, or only its header bar while minimized.</summary>
public sealed record ChatDockWindow(Guid ConversationId, bool Minimized = false);

/// <summary>
/// The mini chats at the bottom of the screen, kept per user across reloads. Without a dock on screen (no layout, no chat permission)
/// opening a chat goes to the chat page instead.
/// </summary>
public sealed class ChatDock(IChatApi api, IJSRuntime js, NavigationManager navigation, CoworkeePages links)
{
    public const int MaxWindows = 3;
    private List<ChatDockWindow> _windows = [];
    private string? _storageKey;

    public IReadOnlyList<ChatDockWindow> Windows => _windows;

    /// <summary>A dock is on screen and shows the windows.</summary>
    public bool Available => _storageKey is not null;

    public event Action? Changed;

    /// <summary>Called by the dock on screen: restores the windows of this user.</summary>
    public async Task AttachAsync(Guid userId)
    {
        _storageKey = $"coworkee.chat.dock.{userId:N}";
        try
        {
            if (await js.InvokeAsync<string?>("localStorage.getItem", _storageKey) is { Length: > 0 } json)
            {
                _windows = JsonSerializer.Deserialize<List<ChatDockWindow>>(json, JsonSerializerOptions.Web)?.Take(MaxWindows).ToList() ?? [];
            }
        }
        catch (Exception exception) when (exception is JSException or JsonException or InvalidOperationException)
        {
            _windows = [];
        }

        Changed?.Invoke();
    }

    public void Detach() => _storageKey = null;

    /// <summary>Shows the chat as an open window, the newest at the right; the oldest window goes when there are too many.</summary>
    public async Task OpenAsync(Guid conversationId)
    {
        if (!Available)
        {
            navigation.NavigateTo(links.Href($"/chat?c={conversationId}") ?? "/chat");
            return;
        }

        _windows.RemoveAll(w => w.ConversationId == conversationId);
        _windows.Add(new ChatDockWindow(conversationId));
        if (_windows.Count > MaxWindows)
        {
            _windows.RemoveAt(0);
        }

        await SaveAsync();
    }

    /// <summary>Opens the direct chat with a person, creating it on first use.</summary>
    public async Task OpenWithAsync(Guid userId) => await OpenAsync((await api.OpenDirectAsync(userId)).Id);

    public async Task ToggleAsync(Guid conversationId)
    {
        _windows = [.. _windows.Select(w => w.ConversationId == conversationId ? w with { Minimized = !w.Minimized } : w)];
        await SaveAsync();
    }

    public async Task CloseAsync(Guid conversationId)
    {
        _windows.RemoveAll(w => w.ConversationId == conversationId);
        await SaveAsync();
    }

    /// <summary>Closes the window and shows the chat on the chat page.</summary>
    public async Task OpenFullAsync(Guid conversationId)
    {
        await CloseAsync(conversationId);
        navigation.NavigateTo(links.Href($"/chat?c={conversationId}") ?? "/chat");
    }

    public bool IsOpen(Guid conversationId) => _windows.Any(w => w.ConversationId == conversationId && !w.Minimized);

    private async Task SaveAsync()
    {
        Changed?.Invoke();
        if (_storageKey is null)
        {
            return;
        }

        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", _storageKey, JsonSerializer.Serialize(_windows, JsonSerializerOptions.Web));
        }
        catch (JSException)
        {
        }
    }
}
