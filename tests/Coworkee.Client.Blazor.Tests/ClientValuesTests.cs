using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Tests;

/// <summary><see cref="IClientValues"/>: read once for every caller, written through the API, kept live over the user's and the organisation's topics.</summary>
public sealed class ClientValuesTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly FakeStore _store = new();
    private readonly FakeRealtimeConnection _realtime = new();
    private readonly ClientValues _values;
    private readonly List<ClientValueDto> _changes = [];

    public ClientValuesTests()
    {
        _values = new ClientValues(new ClientValuesApi(new HttpClient(_store) { BaseAddress = new Uri("https://app.test/") }), new RealtimeClient(_realtime), new SignedIn());
        _values.Changed += _changes.Add;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_read_shares_one_request_and_generic_reads_take_json()
    {
        _store.Values["layout.Mine"] = new ClientValueDto("layout.Mine", """{"columns":["eta"],"size":3}""", true);
        _store.Values["note"] = new ClientValueDto("note", "plain text", false);

        (await _values.GetAllAsync(Ct)).Select(v => v.Key).ShouldBe(["layout.Mine", "note"]);
        (await _values.FindAsync("note", Ct)).ShouldBe(new ClientValueDto("note", "plain text", false));
        (await _values.GetAsync<Layout>("layout.Mine", Ct))!.Columns.ShouldBe(["eta"]);
        (await _values.GetAsync<Layout>("missing", Ct)).ShouldBeNull();
        (await _values.FindAsync("missing", Ct)).ShouldBeNull();

        _store.Gets.ShouldBe(1);
    }

    [Fact]
    public async Task Strings_are_stored_as_they_are_and_objects_as_json()
    {
        await _values.SetAsync("note", "plain text", cancellationToken: Ct);
        await _values.SetAsync("layout.Mine", new Layout(["eta"], 3), ClientValueScope.Global, Ct);

        _store.Puts.ShouldBe([new SetClientValueRequest("note", "plain text"), new SetClientValueRequest("layout.Mine", """{"columns":["eta"],"size":3}""", ClientValueScope.Global)]);
    }

    [Fact]
    public async Task An_own_write_shows_at_once_and_raises_the_change()
    {
        await _values.GetAllAsync(Ct);

        await _values.SetAsync("template.Air & more/1", "B1", cancellationToken: Ct);
        await _values.DeleteAsync("template.Air & more/1", ClientValueScope.User, Ct);

        _store.Deletes.ShouldBe(["key=template.Air%20%26%20more%2F1&scope=User"]);
        _changes.ShouldBe([new ClientValueDto("template.Air & more/1", "B1", true), new ClientValueDto("template.Air & more/1", null, false)]);
        (await _values.GetAllAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Changes_elsewhere_arrive_over_the_users_and_the_organisations_topic()
    {
        _store.Values["shared"] = new ClientValueDto("shared", "1", false);
        await _values.GetAllAsync(Ct);
        await WaitAsync(() => _realtime.Invocations.Contains((RealtimeHubMethods.Subscribe, SettingTopics.ClientValues))
            && _realtime.Invocations.Contains((RealtimeHubMethods.Subscribe, RealtimeTopics.User(UserId))));

        // another tab saved an own value over the global one
        _store.Values["shared"] = new ClientValueDto("shared", "mine", true);
        _realtime.Push(RealtimeTopics.User(UserId), RealtimeEventTypes.EntityChanged, new EntityChangedPayload("ClientValue", "1", EntityChangedPayload.Updated, []));
        await WaitAsync(() => _changes.Count == 1);
        _changes.Single().ShouldBe(new ClientValueDto("shared", "mine", true));

        // other events of the user's topic load nothing
        var gets = _store.Gets;
        _realtime.Push(RealtimeTopics.User(UserId), RealtimeEventTypes.EntityChanged, new EntityChangedPayload("Notification", "2", EntityChangedPayload.Created, []));
        _store.Gets.ShouldBe(gets);

        // an administrator added a global value
        _store.Values["new"] = new ClientValueDto("new", "x", false);
        _realtime.Push(SettingTopics.ClientValues);
        await WaitAsync(() => _changes.Count == 2);
        _changes[1].ShouldBe(new ClientValueDto("new", "x", false));
        (await _values.FindAsync("new", Ct))!.Value.ShouldBe("x");
    }

    [Fact]
    public async Task A_failed_load_is_tried_again()
    {
        _store.Fail = true;
        await Should.ThrowAsync<Api.ApiException>(() => _values.GetAllAsync(Ct));

        _store.Fail = false;
        _store.Values["k"] = new ClientValueDto("k", "v", true);

        (await _values.GetAllAsync(Ct)).Single().Key.ShouldBe("k");
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(50, Ct);
        }

        condition().ShouldBeTrue();
    }

    private sealed record Layout(IReadOnlyList<string> Columns, int Size = 0);

    private sealed class SignedIn : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test"))));
    }

    /// <summary>The server's merge in one dictionary; writes are the caller's own values.</summary>
    private sealed class FakeStore : HttpMessageHandler
    {
        public Dictionary<string, ClientValueDto> Values { get; } = new(StringComparer.Ordinal);

        public List<SetClientValueRequest> Puts { get; } = [];

        public List<string> Deletes { get; } = [];

        public int Gets { get; private set; }

        public bool Fail { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            if (request.Method == HttpMethod.Get)
            {
                Gets++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Values.Values.OrderBy(v => v.Key, StringComparer.Ordinal).ToArray(), options: CoworkeeJson.Web),
                };
            }

            if (request.Method == HttpMethod.Put)
            {
                var put = (await request.Content!.ReadFromJsonAsync<SetClientValueRequest>(CoworkeeJson.Web, cancellationToken))!;
                Puts.Add(put);
                Values[put.Key] = new ClientValueDto(put.Key, put.Value, put.Scope == ClientValueScope.User);
            }
            else
            {
                var query = request.RequestUri!.Query.TrimStart('?');
                Deletes.Add(query);
                Values.Remove(Uri.UnescapeDataString(query.Split('&')[0]["key=".Length..]));
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
