using Coworkee.Client.Blazor.Ai;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>
/// The navigation entry of the assistant and old links to its page: open the assistant window over the page the user came from
/// (over the home page for a link opened on its own).
/// </summary>
public partial class Assistant
{
    [Inject] private AssistantState State { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    protected override void OnInitialized() => State.Open(AssistantSize.Large);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            await module.InvokeVoidAsync("backOrHome");
        }
    }
}
