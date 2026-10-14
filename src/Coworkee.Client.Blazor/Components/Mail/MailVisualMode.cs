using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>The visual editor rewrites markup, so it is only offered for bodies it cannot damage.</summary>
public static partial class MailVisualMode
{
    public static bool Allowed(string body) => !Logic().IsMatch(body) && !body.Contains("<table", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\{\{[-~]?\s*(if|for|while|case|capture|with|func|tablerow)\b")]
    private static partial Regex Logic();
}
