using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>A dialog with MudEx's initial class stays hidden until MudEx shows it: it must be opened with ShowExAsync, not ShowAsync.</summary>
public sealed partial class DialogSourceTests
{
    private static readonly string Sources = Path.GetFullPath(Path.Combine(Here(), "..", "..", "src"));

    [Fact]
    public void Dialogs_with_the_mudex_initial_class_are_opened_with_ShowExAsync()
    {
        var files = Directory.GetFiles(Sources, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => f, File.ReadAllText);
        var dialogs = files.Where(f => f.Key.EndsWith(".razor") && f.Value.Contains("Dialog.Initial")).Select(f => Path.GetFileNameWithoutExtension(f.Key)).ToList();

        dialogs.ShouldNotBeEmpty();
        files.SelectMany(f => PlainShow().Matches(f.Value).Select(m => m.Groups[1].Value)).Where(dialogs.Contains).ShouldBeEmpty();
    }

    /// <summary>Plain MudBlazor dialogs (opened with ShowAsync) that must not carry the class, with the reason; empty while every dialog opens through MudEx.</summary>
    private static readonly Dictionary<string, string> PlainDialogs = [];

    [Fact]
    public void Every_dialog_starts_hidden_with_the_mudex_initial_class()
    {
        // without the class MudEx's open animation starts on a dialog that is already visible: it flashes in place first
        var missing = Directory.GetFiles(Sources, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => MudDialog().IsMatch(File.ReadAllText(f)) && !File.ReadAllText(f).Contains("Dialog.Initial"))
            .Select(Path.GetFileNameWithoutExtension).Where(name => !PlainDialogs.ContainsKey(name!));

        missing.ShouldBeEmpty();
    }

    [GeneratedRegex(@"<MudDialog[\s>]")]
    private static partial Regex MudDialog();

    [GeneratedRegex(@"\.ShowAsync<(\w+)>")]
    private static partial Regex PlainShow();

    private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
