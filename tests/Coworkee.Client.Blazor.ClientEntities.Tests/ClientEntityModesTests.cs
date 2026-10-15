using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public sealed class ClientEntityModesTests
{
    private readonly ICoworkeeApi _api = Substitute.For<ICoworkeeApi>();
    private readonly ClientEntityModes _modes;

    public ClientEntityModesTests() => _modes = new ClientEntityModes(_api, NullLogger<ClientEntityModes>.Instance);

    private static SettingValueDto[] Stored(string json) => [new(TableSettings.ClientEntityModes, json, true)];

    [Fact]
    public async Task Choices_made_while_the_stored_ones_cannot_be_read_do_not_overwrite_them()
    {
        _api.GetSettingsAsync(SettingScope.User, default).ThrowsAsyncForAnyArgs(new HttpRequestException("offline"));

        await _modes.SetAsync("Gadgets", ClientEntityMode.Server);

        (await _modes.GetAsync("Gadgets")).ShouldBe(ClientEntityMode.Server);
        await _api.DidNotReceiveWithAnyArgs().SetSettingsAsync(default, default!, default);

        _api.GetSettingsAsync(SettingScope.User, default).ReturnsForAnyArgs(Stored("{\"Orders\":\"Server\"}"));
        await _modes.SetAsync("Tools", ClientEntityMode.Server);

        await _api.Received(1).SetSettingsAsync(SettingScope.User,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v[TableSettings.ClientEntityModes]!.Contains("\"Orders\":\"Server\"")
                && v[TableSettings.ClientEntityModes]!.Contains("\"Gadgets\":\"Server\"") && v[TableSettings.ClientEntityModes]!.Contains("\"Tools\":\"Server\"")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clearing_reads_the_choices_of_the_next_user()
    {
        _api.GetSettingsAsync(SettingScope.User, default).ReturnsForAnyArgs(Stored("{\"Gadgets\":\"Server\"}"));
        (await _modes.GetAsync("Gadgets")).ShouldBe(ClientEntityMode.Server);

        _api.GetSettingsAsync(SettingScope.User, default).ReturnsForAnyArgs([]);
        (await _modes.GetAsync("Gadgets")).ShouldBe(ClientEntityMode.Server, "kept until cleared");
        _modes.Clear();

        (await _modes.GetAsync("Gadgets")).ShouldBe(ClientEntityMode.Client);
    }

    [Fact]
    public async Task The_browser_storage_releases_its_module_once()
    {
        var js = Substitute.For<Microsoft.JSInterop.IJSRuntime>();
        var module = Substitute.For<Microsoft.JSInterop.IJSObjectReference>();
        js.InvokeAsync<Microsoft.JSInterop.IJSObjectReference>("import", Arg.Any<object?[]?>()).Returns(module);
        var storage = new IndexedDbClientEntityStorage(js);
        await storage.ClearAsync();

        await storage.DisposeAsync();
        await storage.DisposeAsync();
        storage.Dispose();

        await module.Received(1).DisposeAsync();
    }
}
