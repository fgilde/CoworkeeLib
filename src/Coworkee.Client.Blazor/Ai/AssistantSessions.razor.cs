using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>The conversations of the user: search, open, rename in place and delete.</summary>
public partial class AssistantSessions
{
    private string? _search;
    private Guid? _renaming;
    private string? _title;

    [Parameter, EditorRequired] public IReadOnlyList<AiSessionDto> Sessions { get; set; } = [];

    [Parameter] public Guid? Current { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public EventCallback<AiSessionDto> OnSelect { get; set; }

    [Parameter] public EventCallback<(AiSessionDto Session, string Title)> OnRename { get; set; }

    [Parameter] public EventCallback<AiSessionDto> OnDelete { get; set; }

    [Parameter] public EventCallback<string?> OnSearch { get; set; }

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    private Task SearchAsync(string? search)
    {
        _search = search;
        return OnSearch.InvokeAsync(search);
    }

    private void StartRename(AiSessionDto session)
    {
        _renaming = session.Id;
        _title = session.Title;
    }

    private async Task RenameAsync(AiSessionDto session)
    {
        var title = _title?.Trim();
        _renaming = null;
        if (!string.IsNullOrEmpty(title) && title != session.Title)
        {
            await OnRename.InvokeAsync((session, title));
        }
    }

    private Task RenameKeyAsync(KeyboardEventArgs args, AiSessionDto session)
    {
        if (args.Key == "Escape")
        {
            _renaming = null;
        }

        return args.Key == "Enter" ? RenameAsync(session) : Task.CompletedTask;
    }

    private Task SelectKeyAsync(KeyboardEventArgs args, AiSessionDto session) =>
        args.Key is "Enter" or " " ? OnSelect.InvokeAsync(session) : Task.CompletedTask;
}
