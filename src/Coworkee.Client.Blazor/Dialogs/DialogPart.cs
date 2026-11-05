namespace Coworkee.Client.Blazor.Dialogs;

/// <summary>The kinds of dialogs the library opens; each gets its options from <see cref="CoworkeeDialogOptions"/>.</summary>
public enum DialogPart
{
    /// <summary>Edit forms and detail sheets: full height, slide in from the side the user clicked on.</summary>
    SideSheet,

    /// <summary>A narrow full height sheet that always slides in from the right, like the about dialog.</summary>
    RightSheet,

    /// <summary>Small centered dialogs such as confirmations.</summary>
    Small,

    /// <summary>Editors opened over the whole screen, like the code editor.</summary>
    Fullscreen,

    /// <summary>Progress that cannot be closed until it ends, like an export.</summary>
    Progress,

    /// <summary>An item of a list edited in a settings form.</summary>
    ListItem,
}
