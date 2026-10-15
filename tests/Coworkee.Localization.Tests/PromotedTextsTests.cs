using System.Net.Http.Json;
using Coworkee.Contracts.Localization;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Localization.Tests;

public sealed class PromotedTextsTests(LocalizationApp app) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Shipped_texts_replace_machine_translations_stored_while_nothing_shipped_them_but_not_edits()
    {
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalizationTestDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.AddRange(
                new TextKey { Key = "Drop files here", FirstSeenAt = now },
                new TextKey { Key = "Drop/Paste files here", FirstSeenAt = now },
                new TextKey { Key = "Still unshipped", FirstSeenAt = now },
                new Translation { Culture = "de", Key = "Drop files here", Value = "Drop Dateien hier" },
                new Translation { Culture = "fi", Key = "Drop files here", Value = "Pudota tiedostot tähän" },
                new Translation { Culture = "de", Key = "Drop/Paste files here", Value = "Maschine" },
                new Translation { Culture = "de", Key = "Still unshipped", Value = "Noch nicht ausgeliefert" });
            await db.SaveChangesAsync(Ct);

            // an administrator corrected this one
            (await db.Set<Translation>().SingleAsync(t => t.Key == "Drop/Paste files here", Ct)).Value = "Hier ablegen";
            await db.SaveChangesAsync(Ct);
        }

        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<PromotedTexts>().ApplyAsync(Ct)).ShouldBe(1);
        }

        var german = (await app.Anonymous().GetFromJsonAsync<TextsDto>("/api/v1/localization/texts/de", Ct))!.Texts;
        german["Drop files here"].ShouldBe("Dateien hier ablegen");
        german["Drop/Paste files here"].ShouldBe("Hier ablegen");
        german["Still unshipped"].ShouldBe("Noch nicht ausgeliefert");
        (await app.Anonymous().GetFromJsonAsync<TextsDto>("/api/v1/localization/texts/fi", Ct))!.Texts["Drop files here"].ShouldBe("Pudota tiedostot tähän");

        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            var keys = await scope.ServiceProvider.GetRequiredService<LocalizationTestDbContext>().Set<TextKey>().Select(k => k.Key).ToListAsync(Ct);
            keys.ShouldBe(["Still unshipped"]);
        }
    }
}
