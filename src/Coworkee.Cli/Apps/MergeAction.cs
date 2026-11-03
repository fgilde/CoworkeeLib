namespace Coworkee.Cli.Apps;

internal enum MergeAction
{
    /// <summary>A new file of the template.</summary>
    Add,

    /// <summary>The app's file was the template's: it becomes the new one.</summary>
    Update,

    /// <summary>The app changed the file and the template's change merged in cleanly.</summary>
    Merge,

    /// <summary>The template no longer has the file and the app left it as it was.</summary>
    Delete,

    /// <summary>The app's file stays; <see cref="MergeStep.Content"/> goes next to it as <c>.rej</c>.</summary>
    Conflict,
}
