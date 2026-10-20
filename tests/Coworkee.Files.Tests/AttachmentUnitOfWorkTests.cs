using System.Text;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Files.Attachments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Files.Tests;

/// <summary>Puts the files into the ticket's slot, then fails as asked: "result", "throw" or not at all.</summary>
public sealed record AttachToTicket(Guid Ticket, string Slot, string[] Files, string? Fail = null) : ICommand<Result>;

internal sealed class AttachToTicketHandler(IRecordAttachments attachments) : IHandler<AttachToTicket, Result>
{
    public async Task<Result> HandleAsync(AttachToTicket command, CancellationToken cancellationToken)
    {
        foreach (var file in command.Files)
        {
            var put = await attachments.PutAsync("Tickets", command.Ticket, command.Slot, file, "image/png", new MemoryStream(Encoding.UTF8.GetBytes(file)), cancellationToken);
            if (!put.IsSuccess)
            {
                return put.Error!;
            }
        }

        return command.Fail switch
        {
            "throw" => throw new InvalidOperationException("Failed after the upload."),
            "result" => Error.Conflict("test.failed", "Failed after the upload."),
            _ => Result.Success(),
        };
    }
}

public sealed class AttachmentUnitOfWorkTests(FilesApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _ticket;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _ticket = await app.AddTicketAsync(_setup.AdminUserId, _setup.TenantId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("result")]
    [InlineData("throw")]
    public async Task A_command_that_fails_after_putting_a_file_leaves_neither_row_nor_blob(string fail)
    {
        var blobs = app.BlobCount();

        await Should.NotThrowAsync(async () =>
        {
            try
            {
                (await SendAsync(new AttachToTicket(_ticket, "docs", ["a.png", "b.png"], fail))).IsSuccess.ShouldBeFalse();
            }
            catch (InvalidOperationException) when (fail == "throw")
            {
            }
        });

        (await RowsAsync()).ShouldBeEmpty();
        app.BlobCount().ShouldBe(blobs);
    }

    [Fact]
    public async Task A_command_saves_its_files_with_the_unit_of_work_and_a_single_slot_keeps_the_last_one()
    {
        var blobs = app.BlobCount();

        (await SendAsync(new AttachToTicket(_ticket, "proof", ["first.png", "second.png"]))).IsSuccess.ShouldBeTrue();
        (await SendAsync(new AttachToTicket(_ticket, "proof", ["third.png"]))).IsSuccess.ShouldBeTrue();

        (await RowsAsync()).ShouldBe(["third.png"]);
        app.BlobCount().ShouldBe(blobs + 1);
    }

    [Fact]
    public void A_synchronous_scope_with_the_database_disposes()
    {
        var scope = app.App.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>().ShouldNotBeNull();

        Should.NotThrow(scope.Dispose);
    }

    private Task<List<string>> RowsAsync() =>
        app.InDbAsync(db => db.Set<RecordAttachment>().IgnoreQueryFilters().Where(a => a.EntityId == _ticket).Select(a => a.FileName).ToListAsync(Ct));

    private async Task<Result> SendAsync(AttachToTicket command)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, Ct);
    }
}
