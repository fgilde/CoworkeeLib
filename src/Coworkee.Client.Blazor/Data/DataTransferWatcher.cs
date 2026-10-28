using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Data;

/// <summary>
/// Follows the user's imports and exports over the user's realtime topic: raises <see cref="Changed"/> with the fresh job and offers the
/// download of exports started with <see cref="Announce"/> once their file is ready.
/// </summary>
public sealed class DataTransferWatcher(
    IDataTransferApi api, RealtimeClient realtime, AuthenticationStateProvider authentication, ISnackbar snackbar, FileDownloader downloader, CoworkeeLocalizer L)
    : IAsyncDisposable
{
    private const string EntityType = "DataTransferJob";
    private readonly HashSet<Guid> _announced = [];
    private Task? _started;
    private IAsyncDisposable? _subscription;

    public event Action<DataTransferJobDto>? Changed;

    public Task StartAsync() => _started ??= StartCoreAsync();

    /// <summary>Tells the user when the job is done, with a download button for its file.</summary>
    public Task Announce(DataTransferJobDto job)
    {
        if (job.IsFinished)
        {
            Announce(job, job.Status == DataTransferStatus.Completed);
            return Task.CompletedTask;
        }

        _announced.Add(job.Id);
        snackbar.Add(L["The export runs in the background. You get the file here when it is ready."], Severity.Info);
        return StartAsync();
    }

    public async Task DownloadAsync(DataTransferJobDto job)
    {
        if (job.ResultFileName is { } name)
        {
            await snackbar.RunAsync(async () => await downloader.DownloadAsync(name, ContentType(name), await api.DownloadAsync(job.Id)));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }

    private async Task StartCoreAsync()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (ClientUser.From(user).TryGetUserId(out var userId))
        {
            _subscription = await realtime.SubscribeAsync(RealtimeTopics.User(userId), OnEventAsync);
        }
    }

    private async Task OnEventAsync(RealtimeEnvelope envelope)
    {
        if (envelope.Type != RealtimeEventTypes.EntityChanged
            || envelope.Payload.Deserialize<EntityChangedPayload>(JsonSerializerOptions.Web) is not { EntityType: EntityType } change
            || !Guid.TryParse(change.EntityId, out var id))
        {
            return;
        }

        DataTransferJobDto job;
        try
        {
            job = await api.GetJobAsync(id);
        }
        catch (ApiException)
        {
            return;
        }

        Changed?.Invoke(job);
        if (job.IsFinished && _announced.Remove(job.Id))
        {
            Announce(job, job.Status == DataTransferStatus.Completed);
        }
    }

    private void Announce(DataTransferJobDto job, bool ready)
    {
        if (job.Status == DataTransferStatus.Cancelled)
        {
            return;
        }

        if (!ready)
        {
            snackbar.Add(L["The export of {0} failed: {1}", job.EntitySet, job.Message ?? string.Empty], Severity.Error);
            return;
        }

        snackbar.Add(L["The export of {0} is ready.", job.EntitySet], Severity.Success, options =>
        {
            options.RequireInteraction = true;
            options.Action = L["Download"];
            options.OnClick = _ => DownloadAsync(job);
        });
    }

    private static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".csv" => "text/csv",
        ".json" => "application/json",
        _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };
}
