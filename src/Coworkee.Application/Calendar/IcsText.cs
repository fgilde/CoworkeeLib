using System.Text;

namespace Coworkee.Application.Calendar;

internal static class IcsText
{
    public static string Escape(string text) => text
        .Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
        .Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n");

    public static string Parameter(string value)
    {
        var clean = value.Replace("\"", "").Replace("\r", "").Replace("\n", " ");
        return clean.IndexOfAny([':', ';', ',']) >= 0 ? $"\"{clean}\"" : clean;
    }

    /// <summary>Folds a content line after 75 octets (RFC 5545 3.1); continuation lines start with a space and never split a UTF-8 sequence.</summary>
    public static void AppendFolded(StringBuilder ics, string line)
    {
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > 75)
            {
                ics.Append("\r\n ");
                octets = 1;
            }

            ics.Append(rune.ToString());
            octets += size;
        }

        ics.Append("\r\n");
    }
}
