using Coworkee.Contracts.Mailing;

namespace Coworkee.Client.Blazor.Components.Mail;

internal static class MailVariables
{
    /// <summary>A member of a list element, listed as <c>list[].member</c>.</summary>
    public static bool IsItem(this MailVariableDto variable) => variable.Name.Contains("[]", StringComparison.Ordinal);

    /// <summary>How the variable is written in a template; members of a list element are members of the loop variable of the skeleton.</summary>
    public static string Expression(this MailVariableDto variable) =>
        variable.IsItem() ? "item" + variable.Name[(variable.Name.IndexOf("[]", StringComparison.Ordinal) + 2)..] : variable.Name;
}
