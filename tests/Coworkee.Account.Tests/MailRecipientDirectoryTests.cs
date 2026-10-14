using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Account.Tests;

public sealed class MailRecipientDirectoryTests(AccountApp app) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static User Seed(string? email, Guid tenantId, Action<User>? change = null)
    {
        var user = new User
        {
            TenantId = tenantId,
            UserName = email ?? Guid.NewGuid().ToString(),
            Email = email,
            EmailConfirmed = true,
            CreatedAt = Now.AddDays(-100),
        };
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.NormalizedEmail = email?.ToUpperInvariant();
        change?.Invoke(user);
        return user;
    }

    private async Task<Dictionary<string, Guid>> SaveAsync(params User[] users)
    {
        await app.InScopeAsync(null, _setup.TenantId, async sp =>
        {
            var db = sp.GetRequiredService<CoworkeeDbContext>();
            var created = users.Select(u => (u.Id, u.CreatedAt)).ToList();
            db.AddRange(users);
            await db.SaveChangesAsync(Ct);
            // saving stamps CreatedAt, the tests need their own
            foreach (var (id, createdAt) in created)
            {
                await db.Set<User>().Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedAt, createdAt), Ct);
            }

            return 0;
        });
        return users.Where(u => u.Email is not null).ToDictionary(u => u.Email![..u.Email!.IndexOf('@')], u => u.Id);
    }

    private async Task<(Guid TenantId, Guid[] UserIds)> SeedOtherTenantAsync(params string[] emails)
    {
        var tenant = new Tenant { Name = "Other", Identifier = "other" };
        var users = emails.Select(e => Seed(e, tenant.Id)).ToArray();
        await app.InScopeAsync(null, _setup.TenantId, async sp =>
        {
            var db = sp.GetRequiredService<CoworkeeDbContext>();
            db.Add(tenant);
            db.AddRange(users);
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        return (tenant.Id, [.. users.Select(u => u.Id)]);
    }

    private async Task OptOutAsync(Guid userId, string value = "False") =>
        await app.InScopeAsync(null, _setup.TenantId, async sp =>
        {
            var db = sp.GetRequiredService<CoworkeeDbContext>();
            db.Add(new SettingValue { Name = MailSettings.Newsletters, Scope = SettingScope.User, ScopeKey = userId, Value = value });
            await db.SaveChangesAsync(Ct);
            return 0;
        });

    private async Task<Dictionary<string, Guid>> SeedMixedAsync()
    {
        var users = await SaveAsync(
            Seed("recent@seed.test", _setup.TenantId, u => u.LastLoginAt = Now.AddDays(-5)),
            Seed("stale@seed.test", _setup.TenantId, u => u.LastLoginAt = Now.AddDays(-40)),
            Seed("neverold@seed.test", _setup.TenantId),
            Seed("nevernew@seed.test", _setup.TenantId, u => { u.CreatedAt = Now.AddDays(-2); u.EmailConfirmed = false; }),
            Seed("deactivated@seed.test", _setup.TenantId, u => u.IsActive = false),
            Seed("locked@seed.test", _setup.TenantId, u => u.LockoutEnd = Now.AddDays(1)),
            Seed(null, _setup.TenantId),
            Seed("optedout@seed.test", _setup.TenantId));
        await OptOutAsync(users["optedout"]);
        return users;
    }

    private Task<T> InDirectoryAsync<T>(Func<IMailRecipientDirectory, Task<T>> action) =>
        app.InScopeAsync(_setup.AdminUserId, _setup.TenantId, sp => action(sp.GetRequiredService<IMailRecipientDirectory>()));

    private async Task<string[]> SeededEmailsAsync(RecipientSourceDto source)
    {
        var page = (await InDirectoryAsync(d => d.PageAsync(source, null, 100, Ct))).Value;
        return [.. page.Recipients.Select(r => r.Email).Where(e => e.EndsWith("@seed.test", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
    }

    public static TheoryData<RecipientPreset, int?, string[]> PresetCases => new()
    {
        { RecipientPreset.AllActive, null, ["nevernew@seed.test", "neverold@seed.test", "recent@seed.test", "stale@seed.test"] },
        { RecipientPreset.NotSignedInSince, 30, ["neverold@seed.test", "stale@seed.test"] },
        { RecipientPreset.NeverSignedIn, null, ["nevernew@seed.test", "neverold@seed.test"] },
        { RecipientPreset.SignedUpWithin, 30, ["nevernew@seed.test"] },
        { RecipientPreset.EmailNotConfirmed, null, ["nevernew@seed.test"] },
        { RecipientPreset.Inactive, null, ["deactivated@seed.test"] },
    };

    [Theory]
    [MemberData(nameof(PresetCases))]
    public async Task Presets_select_the_documented_users(RecipientPreset preset, int? days, string[] expected)
    {
        await SeedMixedAsync();

        (await SeededEmailsAsync(new RecipientSourceDto(RecipientSourceKind.Preset, preset, days))).ShouldBe(expected);
    }

    [Fact]
    public async Task InRoles_selects_members_directly_and_through_groups()
    {
        var users = await SeedMixedAsync();
        var roleId = await app.InScopeAsync(null, _setup.TenantId, async sp =>
        {
            var db = sp.GetRequiredService<CoworkeeDbContext>();
            var role = new Role { Name = "readers", NormalizedName = "READERS", TenantId = _setup.TenantId };
            db.Add(role);
            db.Add(new IdentityUserRole<Guid> { UserId = users["recent"], RoleId = role.Id });
            var group = new UserGroup { Name = "team", TenantId = _setup.TenantId };
            group.Members.Add(new UserGroupMember { UserId = users["stale"] });
            group.Roles.Add(new UserGroupRole { RoleId = role.Id });
            db.Add(group);
            await db.SaveChangesAsync(Ct);
            return role.Id;
        });

        (await SeededEmailsAsync(new RecipientSourceDto(RecipientSourceKind.Preset, RecipientPreset.InRoles, RoleIds: [roleId])))
            .ShouldBe(["recent@seed.test", "stale@seed.test"]);
    }

    [Theory]
    [InlineData(RecipientPreset.NotSignedInSince, null)]
    [InlineData(RecipientPreset.SignedUpWithin, 0)]
    [InlineData(RecipientPreset.InRoles, null)]
    public async Task A_preset_without_its_parameter_is_a_validation_error(RecipientPreset preset, int? days)
    {
        var result = await InDirectoryAsync(d => d.PreviewAsync(new RecipientSourceDto(RecipientSourceKind.Preset, preset, days), 5, Ct));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(Coworkee.Core.Results.ErrorKind.Validation);
    }

    [Fact]
    public async Task Locked_mailless_deactivated_and_opted_out_users_are_excluded_and_counted()
    {
        await SeedMixedAsync();

        var preview = (await InDirectoryAsync(d => d.PreviewAsync(new RecipientSourceDto(RecipientSourceKind.Preset, RecipientPreset.AllActive), 20, Ct))).Value;

        preview.Excluded.ShouldBe(4);
        preview.Count.ShouldBe(5);
        preview.First.Select(r => r.Email).Where(e => e.EndsWith("@seed.test", StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .ShouldBe(["nevernew@seed.test", "neverold@seed.test", "recent@seed.test", "stale@seed.test"]);
    }

    [Fact]
    public async Task A_global_opt_out_excludes_everyone_who_did_not_opt_in()
    {
        var users = await SeedMixedAsync();
        await app.SetSettingAsync(MailSettings.Newsletters, "false");
        await OptOutAsync(users["recent"], "TRUE");

        (await SeededEmailsAsync(new RecipientSourceDto(RecipientSourceKind.Preset, RecipientPreset.AllActive))).ShouldBe(["recent@seed.test"]);
    }

    [Fact]
    public async Task Manual_ids_of_another_tenant_are_ignored_and_opted_out_ones_skipped()
    {
        var users = await SeedMixedAsync();
        var (_, foreignIds) = await SeedOtherTenantAsync("foreign@seed.test");

        var source = new RecipientSourceDto(RecipientSourceKind.Manual, UserIds: [users["recent"], users["optedout"], foreignIds[0]]);

        (await SeededEmailsAsync(source)).ShouldBe(["recent@seed.test"]);
        (await InDirectoryAsync(d => d.PreviewAsync(source, 5, Ct))).Value.Excluded.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_filter_is_a_validation_error()
    {
        var result = await InDirectoryAsync(d => d.PreviewAsync(new RecipientSourceDto(RecipientSourceKind.Query, Filter: "nope eq"), 5, Ct));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Details!.ShouldContainKey("Filter");
    }

    [Fact]
    public async Task Query_filter_selects_users_of_the_own_tenant_only()
    {
        await SaveAsync(Seed("ada@seed.test", _setup.TenantId), Seed("bob@seed.test", _setup.TenantId));
        await SeedOtherTenantAsync("anna@seed.test");

        var page = (await InDirectoryAsync(d => d.PageAsync(new RecipientSourceDto(RecipientSourceKind.Query, Filter: "startswith(Email,'a')"), null, 100, Ct))).Value;

        page.Recipients.Select(r => r.Email).Order(StringComparer.Ordinal).ShouldBe(["ada@seed.test", "admin@acme.test"]);
    }

    [Fact]
    public async Task Paging_returns_every_user_once()
    {
        await SaveAsync([.. Enumerable.Range(1, 7).Select(i => Seed($"p{i}@page.test", _setup.TenantId))]);
        var source = new RecipientSourceDto(RecipientSourceKind.Query, Filter: "endswith(Email,'@page.test')");

        var seen = new List<Guid>();
        var sizes = new List<int>();
        Guid? after = null;
        do
        {
            var page = (await InDirectoryAsync(d => d.PageAsync(source, after, 3, Ct))).Value;
            sizes.Add(page.Recipients.Count);
            seen.AddRange(page.Recipients.Select(r => r.Id));
            after = page.Next;
        }
        while (after is not null);

        sizes.ShouldBe([3, 3, 1]);
        seen.Distinct().Count().ShouldBe(7);
    }

    [Fact]
    public async Task Recipients_carry_their_own_language_else_the_system_one()
    {
        var users = await SaveAsync(Seed("de@seed.test", _setup.TenantId), Seed("en@seed.test", _setup.TenantId));
        await app.InScopeAsync(null, _setup.TenantId, async sp =>
        {
            var db = sp.GetRequiredService<CoworkeeDbContext>();
            db.Add(new SettingValue { Name = Coworkee.Contracts.Localization.LocalizationSettings.Culture, Scope = SettingScope.User, ScopeKey = users["de"], Value = "de" });
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        await app.SetSettingAsync(Coworkee.Contracts.Localization.LocalizationSettings.Culture, "fr");

        var page = (await InDirectoryAsync(d => d.PageAsync(new RecipientSourceDto(RecipientSourceKind.Query, Filter: "endswith(Email,'@seed.test')"), null, 10, Ct))).Value;

        page.Recipients.Single(r => r.Email == "de@seed.test").Culture.ShouldBe("de");
        page.Recipients.Single(r => r.Email == "en@seed.test").Culture.ShouldBe("fr");
    }

    [Fact]
    public async Task Recipient_lists_are_per_tenant()
    {
        var otherTenant = (await SeedOtherTenantAsync("someone@seed.test")).TenantId;
        var source = new RecipientSourceDto(RecipientSourceKind.Preset, RecipientPreset.AllActive);
        var foreignId = await app.InScopeAsync(null, otherTenant, async sp =>
        {
            var db = sp.GetRequiredService<CoworkeeDbContext>();
            var foreign = new RecipientList { TenantId = otherTenant, Name = "Everyone", Source = "{\"kind\":\"preset\",\"preset\":\"AllActive\"}", CreatedAt = Now };
            db.Add(foreign);
            await db.SaveChangesAsync(Ct);
            return foreign.Id;
        });
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);

        (await admin.GetFromJsonAsync<RecipientListDto[]>("/api/v1/mail/recipient-lists", Ct))!.ShouldBeEmpty();
        (await admin.DeleteAsync($"/api/v1/mail/recipient-lists/{foreignId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync($"/api/v1/mail/recipient-lists/{foreignId}", new SaveRecipientListRequest("Hijacked", source), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var created = await admin.PostAsJsonAsync("/api/v1/mail/recipient-lists", new SaveRecipientListRequest("Everyone", source), Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = (await created.Content.ReadFromJsonAsync<RecipientListDto>(Ct))!;
        (await admin.PostAsJsonAsync("/api/v1/mail/recipient-lists", new SaveRecipientListRequest("Everyone", source), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync($"/api/v1/mail/recipient-lists/{list.Id}", new SaveRecipientListRequest("Renamed", source with { Preset = RecipientPreset.NeverSignedIn }), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var lists = (await admin.GetFromJsonAsync<RecipientListDto[]>("/api/v1/mail/recipient-lists", Ct))!;
        lists.Single().Name.ShouldBe("Renamed");
        lists.Single().Source.Preset.ShouldBe(RecipientPreset.NeverSignedIn);
        (await admin.DeleteAsync($"/api/v1/mail/recipient-lists/{list.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_preview_endpoint_answers_with_counts_and_a_bad_filter_with_400()
    {
        await SeedMixedAsync();
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);

        var ok = await admin.PostAsJsonAsync("/api/v1/mail/recipients/preview", new RecipientSourceDto(RecipientSourceKind.Preset, RecipientPreset.AllActive), Ct);
        (await ok.Content.ReadFromJsonAsync<RecipientPreviewDto>(Ct))!.Count.ShouldBe(5);
        (await admin.PostAsJsonAsync("/api/v1/mail/recipients/preview", new RecipientSourceDto(RecipientSourceKind.Query, Filter: "nope eq"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> SenderWithoutUsersRightAsync()
    {
        var userId = await app.InScopeAsync(null, _setup.TenantId, async sp =>
        {
            var db = sp.GetRequiredService<CoworkeeDbContext>();
            var sender = Seed("sender@seed.test", _setup.TenantId);
            db.Add(sender);
            db.Add(new PermissionGrant { TenantId = _setup.TenantId, Name = MailPermissions.Campaigns.Send, ProviderType = PermissionProviderType.User, ProviderKey = sender.Id });
            await db.SaveChangesAsync(Ct);
            return sender.Id;
        });
        return app.As(userId, _setup.TenantId);
    }

    [Fact]
    public async Task Choosing_by_filter_or_by_hand_needs_the_right_to_view_users()
    {
        var users = await SeedMixedAsync();
        var sender = await SenderWithoutUsersRightAsync();

        foreach (var source in new[]
        {
            new RecipientSourceDto(RecipientSourceKind.Query, Filter: "startswith(Email,'a')"),
            new RecipientSourceDto(RecipientSourceKind.Manual, UserIds: [users["recent"]]),
        })
        {
            (await sender.PostAsJsonAsync("/api/v1/mail/recipients/preview", source, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, source.Kind.ToString());
        }
    }

    [Fact]
    public async Task A_preset_preview_without_the_right_to_view_users_has_counts_but_no_rows()
    {
        await SeedMixedAsync();
        var sender = await SenderWithoutUsersRightAsync();

        var response = await sender.PostAsJsonAsync("/api/v1/mail/recipients/preview", new RecipientSourceDto(RecipientSourceKind.Preset, RecipientPreset.AllActive), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = (await response.Content.ReadFromJsonAsync<RecipientPreviewDto>(Ct))!;
        preview.Count.ShouldBeGreaterThan(0);
        preview.First.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_undefined_source_kind_is_a_validation_error()
    {
        var result = await InDirectoryAsync(d => d.PreviewAsync(new RecipientSourceDto((RecipientSourceKind)99), 5, Ct));

        result.Error!.Details!.ShouldContainKey("Kind");
    }
}