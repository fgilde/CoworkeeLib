namespace Coworkee.Client.Blazor.Theming;

/// <summary>Anything the rules editor does not edit (at-rules, comments, nested or unparseable CSS), kept verbatim.</summary>
public sealed record CssRawBlock(string Text) : CssBlock;
