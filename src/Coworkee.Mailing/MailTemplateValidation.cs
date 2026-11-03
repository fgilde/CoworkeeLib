using Microsoft.EntityFrameworkCore;
using Scriban;

namespace Coworkee.Mailing;

public static class MailTemplateValidation
{
    public static IReadOnlyList<string> Validate(string subject, string body) =>
        Errors("subject", subject).Concat(Errors("body", body)).ToList();

    private static IEnumerable<string> Errors(string part, string text)
    {
        var template = Template.Parse(text);
        return template.HasErrors ? template.Messages.Select(m => $"{part}: {m}") : [];
    }
}
