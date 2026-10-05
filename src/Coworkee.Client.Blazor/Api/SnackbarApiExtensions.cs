using MudBlazor;

namespace Coworkee.Client.Blazor.Api;

public static class SnackbarApiExtensions
{
    /// <summary>Runs an API call and shows its failure (validation messages first) instead of throwing.</summary>
    public static async Task<bool> RunAsync(this ISnackbar snackbar, Func<Task> action, string? success = null)
    {
        try
        {
            await action();
            if (success is not null)
            {
                snackbar.Add(success, Severity.Success);
            }

            return true;
        }
        catch (ApiException exception)
        {
            snackbar.Add(Describe(exception), Severity.Error);
            return false;
        }
    }

    public static string Describe(ApiException exception) =>
        exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message;
}
