using Elsa.Workflows;

namespace Coworkee.Workflows.Elsa.Activities;

internal static class ElsaValues
{
    /// <summary>A Guid from the workflow input, which arrives as a Guid, a string or a JSON value depending on how it was stored.</summary>
    public static Guid? Guid(ActivityExecutionContext context, string key) =>
        context.WorkflowInput.TryGetValue(key, out var value) && System.Guid.TryParse(value?.ToString(), out var id) ? id : null;

    public static string? Text(ActivityExecutionContext context, string key) =>
        context.WorkflowInput.TryGetValue(key, out var value) ? value?.ToString() : null;
}
