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

    /// <summary>The validation messages, else the problem's title, else its code (<see cref="ApiException"/>'s message).</summary>
    public static string Describe(ApiException exception) => exception.Message;
}
