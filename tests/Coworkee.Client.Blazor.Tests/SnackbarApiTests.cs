using Coworkee.Client.Blazor.Api;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SnackbarApiTests
{
    [Fact]
    public async Task Failures_show_the_validation_messages_and_successes_the_given_text()
    {
        var snackbar = Substitute.For<ISnackbar>();

        (await snackbar.RunAsync(() => throw new ApiException(400, "validation", new Dictionary<string, string[]> { ["Name"] = ["Name is required."] }))).ShouldBeFalse();
        (await snackbar.RunAsync(() => Task.CompletedTask, "Saved")).ShouldBeTrue();

        snackbar.Received(1).Add("Name is required.", Severity.Error, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
        snackbar.Received(1).Add("Saved", Severity.Success, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
    }
}
