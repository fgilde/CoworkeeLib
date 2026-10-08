using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.People;

/// <summary>
/// Names and pictures of the people of the organisation, loaded in batches and kept up to date: when someone changes their name
/// or picture, every avatar and card showing them updates.
/// </summary>
public sealed class UserCards(ICoworkeeApi api, RealtimeClient realtime) : IAsyncDisposable
{
    private readonly Dictionary<Guid, Task<UserCardDto?>> _cards = [];
    private readonly HashSet<Guid> _queued = [];
    private TaskCompletionSource<IReadOnlyDictionary<Guid, UserCardDto>>? _batch;
    private IAsyncDisposable? _subscription;

    public event Action<Guid>? Changed;

    public Task<UserCardDto?> GetAsync(Guid userId)
    {
        _ = SubscribeAsync();
        if (!_cards.TryGetValue(userId, out var card))
        {
            card = QueueAsync(userId);
            _cards[userId] = card;
        }

        return card;
    }

    /// <summary>The picture URL with its version, so a changed picture is fetched again.</summary>
    public static string AvatarUrl(UserCardDto card) => $"api/v1/identity/users/{card.Id}/avatar?v={card.AvatarVersion}";

    public void Forget(Guid userId)
    {
        _cards.Remove(userId);
        Changed?.Invoke(userId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }

    private async Task<UserCardDto?> QueueAsync(Guid userId)
    {
        _queued.Add(userId);
        var batch = _batch ??= StartBatch();
        return (await batch.Task).GetValueOrDefault(userId);
    }

    // ponytail: collects the ids of one render pass for 30 ms, so a list of 50 rows asks once
    private TaskCompletionSource<IReadOnlyDictionary<Guid, UserCardDto>> StartBatch()
    {
        var batch = new TaskCompletionSource<IReadOnlyDictionary<Guid, UserCardDto>>();
        _ = Task.Run(async () =>
        {
            await Task.Delay(30);
            var ids = _queued.ToList();
            _queued.Clear();
            _batch = null;
            try
            {
                batch.SetResult((await api.GetUserCardsAsync(ids)).ToDictionary(c => c.Id));
            }
            catch (Exception exception) when (exception is ApiException or HttpRequestException)
            {
                batch.SetResult(new Dictionary<Guid, UserCardDto>());
            }
        });
        return batch;
    }

    private async Task SubscribeAsync()
    {
        if (_subscription is not null)
        {
            return;
        }

        _subscription = NoSubscription.Instance;
        _subscription = await realtime.SubscribeAsync(UserEvents.Topic, envelope =>
        {
            if (envelope.Payload.TryGetProperty("userId", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var userId))
            {
                Forget(userId);
            }

            return Task.CompletedTask;
        });
    }

    private sealed class NoSubscription : IAsyncDisposable
    {
        public static readonly NoSubscription Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
