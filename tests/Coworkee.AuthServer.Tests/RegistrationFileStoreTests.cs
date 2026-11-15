using Coworkee.Account;
using Coworkee.Files;
using Coworkee.Identity.Domain;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

/// <summary>The whole way of a registration document on an auth server that depends on CoworkeeFileRegistrationDocumentsModule: upload, folder, blob.</summary>
public sealed class RegistrationFileStoreTests(AuthAppWithFiles app) : IClassFixture<AuthAppWithFiles>, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await app.SetupAsync();
        await app.SetSettingAsync(AccountSettings.AllowRegistration, "true");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_uploads_land_in_the_registration_folder_of_the_new_user_with_their_content()
    {
        var wizard = new RegistrationWizard(app.Browser());

        await wizard.FillAsync("ivy@acme.test");
        wizard.Step.ShouldBe("documents");
        await wizard.SubmitAsync(("Document0", "passport.png", "image/png", 10), ("Document1", "cert.pdf", "application/pdf", 20));

        wizard.Errors.ShouldBeEmpty();
        var user = await app.InDbAsync(db => db.Set<User>().SingleAsync(u => u.Email == "ivy@acme.test", Ct));
        var files = await app.InDbAsync(db => db.Set<StoredFile>().IgnoreQueryFilters().OrderBy(f => f.Name).ToListAsync(Ct));
        files.Select(f => (f.Name, f.ContentType, f.Size, f.CreatedBy)).ShouldBe([
            ("Certificate - cert.pdf", "application/pdf", 20L, (Guid?)user.Id),
            ("Passport - passport.png", "image/png", 10L, user.Id),
        ]);
        var folder = await app.InDbAsync(db => db.Set<FileFolder>().IgnoreQueryFilters().SingleAsync(f => f.Id == files[0].FolderId, Ct));
        folder.Name.ShouldBe("ivy@acme.test");
        (await app.InDbAsync(db => db.Set<FileFolder>().IgnoreQueryFilters().SingleAsync(f => f.Id == folder.ParentId, Ct))).Name.ShouldBe("Registrations");

        var storage = app.App.Services.GetRequiredService<IBlobStorage>();
        foreach (var file in files)
        {
            await using var content = await storage.OpenReadAsync(file.BlobKey, Ct);
            content.ShouldNotBeNull();
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, Ct);
            copy.Length.ShouldBe(file.Size);
        }
    }
}
