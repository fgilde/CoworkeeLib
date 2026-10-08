using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;

namespace Coworkee.Ai.Tests;

public sealed class ToolDiscoveryTests
{
    [Fact]
    public void Requests_with_a_permission_or_the_marker_become_tools_unless_excluded()
    {
        var registered = new AiTool("add_note", "Adds a note.", typeof(AddNote), typeof(Result<Guid>));

        var catalog = new AiToolCatalog([registered], [new TestAiModule()]);

        var tools = catalog.Tools.ToDictionary(t => t.Name);
        tools["add_note"].Description.ShouldBe("Adds a note.");
        tools["archive_notes"].Description.ShouldBe("Archives notes older than the given date.");
        tools["archive_notes"].Permissions.ShouldBe(["Notes.Archive"]);
        tools["archive_notes"].ReadOnly.ShouldBeFalse();
        tools["count_notes"].Description.ShouldBe("Counts the notes.");
        tools["count_notes"].ReadOnly.ShouldBeTrue();
        tools.Keys.ShouldNotContain("secret_notes");
        tools.Keys.ShouldNotContain("plain_notes");
        tools.Values.Count(t => t.RequestType == typeof(AddNote)).ShouldBe(1);
    }
}

[RequiresPermission("Notes.Archive")]
[Description("Archives notes older than the given date.")]
public sealed record ArchiveNotesCommand(DateTimeOffset OlderThan) : ICommand<Result>;

[AiTool("Counts the notes.")]
public sealed record CountNotesQuery : IQuery<Result<int>>;

[AiTool(Exclude = true)]
[RequiresPermission("Notes.Archive")]
public sealed record SecretNotesQuery : IQuery<Result<int>>;

public sealed record PlainNotesQuery : IQuery<Result<int>>;
