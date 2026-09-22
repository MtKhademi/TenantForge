using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B039's tenant storefront profile/policy surface: the admin GET
/// (null-profile empty state), the admin PUT (create/update with optimistic
/// concurrency via ExpectedVersion), the unique-tenant create race, tenant
/// isolation, the Shop.Settings.Manage permission gate (Owner bypass + role
/// grant), text/phone/Instagram-URL validation, and the anonymous public GET
/// (published-only, no Version in the wire shape).
///
/// Runs on a dedicated database (ShopProfileIsolatedCollection) so its
/// tenants and profile rows never inflate other test classes' row counts.
/// </summary>
[Collection(nameof(ShopProfileIsolatedCollection))]
public sealed class ShopProfileIntegrationTests(ShopProfileDbFixture db) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    private async Task<HttpClient> PlatformAdminClientAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = ApiFactory.Password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", document.RootElement.GetProperty("accessToken").GetString()!);
        return client;
    }

    private async Task<Tsid> CreateOwnerAccountAsync(string email)
    {
        await using var context = db.CreateContext();
        var account = Account.CreateUser(email, "Shop Owner", "already-hashed-for-test", DateTimeOffset.UtcNow);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<string> CreateTenantWithOwnerAsync(HttpClient adminClient, Tsid ownerAccountId, string name)
    {
        var response = await adminClient.PostAsJsonAsync("/api/platform/tenants", new
        {
            name,
            slug = $"shop-{Guid.NewGuid():N}"[..18],
            ownerUserId = TsidId.Format(ownerAccountId)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static void SetMemberToken(HttpClient client, Tsid accountId, string email)
    {
        var token = TestJwtFactory.Issue(
            signingKey: ApiFactory.SigningKey,
            subject: TsidId.Format(accountId),
            email: email,
            displayName: "Shop Owner",
            isPlatformAdmin: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<(string TenantId, HttpClient OwnerClient, Tsid OwnerAccount)> NewTenantWithOwnerAsync()
    {
        var admin = await PlatformAdminClientAsync();
        var ownerAccount = await CreateOwnerAccountAsync($"owner-{Guid.NewGuid():N}@tenantforge.local");
        var tenantId = await CreateTenantWithOwnerAsync(admin, ownerAccount, $"Boutique {Guid.NewGuid():N}"[..8]);
        var ownerClient = CreateClient();
        SetMemberToken(ownerClient, ownerAccount, $"owner-{ownerAccount.ToLong():x}@tenantforge.local");
        return (tenantId, ownerClient, ownerAccount);
    }

    private async Task<Tsid> CreateMemberAccountAsync(string tenantId, string email)
    {
        var tenantTsid = TsidId.TryParseNullable(tenantId) ?? throw new InvalidOperationException("tenantId must be a canonical TSID string.");
        await using var context = db.CreateContext();
        var account = Account.CreateUser(email, "Shop Member", "already-hashed-for-test", DateTimeOffset.UtcNow);
        context.Accounts.Add(account);
        context.TenantMemberships.Add(TenantMembership.CreateMember(tenantTsid, account.Id, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return account.Id;
    }

    private async Task GrantRoleAsync(string tenantId, Tsid accountId, IReadOnlyList<string> permissionKeys)
    {
        var tenantTsid = TsidId.TryParseNullable(tenantId) ?? throw new InvalidOperationException("tenantId must be a canonical TSID string.");
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var membership = context.TenantMemberships
            .Single(member => member.TenantId == tenantTsid && member.AccountId == accountId);
        var role = TenantRole.Create(tenantTsid, $"Shop Grant {Guid.NewGuid():N}"[..22], permissionKeys, now);
        context.TenantRoles.Add(role);
        context.TenantMemberRoleAssignments.Add(TenantMemberRoleAssignment.Create(membership.Id, role.Id, now));
        await context.SaveChangesAsync();
    }

    private static object ValidProfileBody(string name, string? instagramUrl = null, int? expectedVersion = null, bool isPublished = true) => new
    {
        name,
        tagline = "A small curated shop",
        supportPhone = "021 8877 6655",
        instagramUrl,
        aboutText = "About us",
        shippingPolicy = "We ship everywhere",
        paymentPolicy = "Cash on delivery",
        returnPolicy = "7 day returns",
        privacyPolicy = "We keep it private",
        isPublished,
        expectedVersion
    };

    private static Task<HttpResponseMessage> PutProfileAsync(HttpClient client, string tenantId, object body) =>
        client.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/profile", body);

    private static async Task<ShopProfileDto?> GetAdminProfileAsync(HttpClient client, string tenantId)
    {
        var response = await client.GetAsync($"/api/tenants/{tenantId}/shop/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ShopProfileEnvelopeDto>();
        Assert.NotNull(envelope);
        return envelope!.Profile;
    }

    /// <summary>
    /// Reads the persisted row through a raw connection — the Shop module's
    /// types are internal to the module, so raw SQL is the honest way to
    /// assert on the actual database state.
    /// </summary>
    private async Task<(string Name, int Version, string? InstagramUrl, bool IsPublished, string AboutText)?> GetStoredProfileAsync(string tenantId)
    {
        var tenantLong = TsidId.TryParse(tenantId, out var tsid) ? tsid.ToLong() : throw new InvalidOperationException("tenantId must be a canonical TSID string.");
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            select name, version, instagram_url, is_published, about_text
            from shop_profiles
            where tenant_id = @tenantId;
            """;
        command.Parameters.Add(new NpgsqlParameter("@tenantId", tenantLong));

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetBoolean(3), reader.GetString(4));
    }

    private Task<int> CountProfilesForTenantAsync(string tenantId)
    {
        var tenantLong = TsidId.TryParse(tenantId, out var tsid) ? tsid.ToLong() : throw new InvalidOperationException("tenantId must be a canonical TSID string.");
        return CountAsync("select count(*) from shop_profiles where tenant_id = @tenantId;", tenantLong);
    }

    private async Task<int> CountAsync(string sql, long tenantLong)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new NpgsqlParameter("@tenantId", tenantLong));
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    [Fact]
    public async Task AdminGet_BeforeAnyProfileExists_Returns200WithNullProfile()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();

        var response = await client.GetAsync($"/api/tenants/{tenantId}/shop/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ShopProfileEnvelopeDto>();
        Assert.NotNull(envelope);
        Assert.Null(envelope!.Profile);

        // The empty state is a real absence of a row, not a hidden row.
        Assert.Equal(0, await CountProfilesForTenantAsync(tenantId));
    }

    [Fact]
    public async Task AdminPut_CreateThenUpdate_RoundTrips_AndBumpsVersion()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();

        // Create: ExpectedVersion null -> a new row at Version 1.
        var createResponse = await PutProfileAsync(client, tenantId, ValidProfileBody("  First Boutique  ", expectedVersion: null));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ShopProfileEnvelopeDto>();
        Assert.NotNull(created?.Profile);
        Assert.Equal("First Boutique", created!.Profile!.Name);
        Assert.Equal(1, created.Profile.Version);
        Assert.True(TsidId.TryParse(created.Profile.Id, out _), "profile id must be a canonical TSID string");
        Assert.Equal(tenantId, created.Profile.TenantId);

        // Update: the version the client just saw -> applied, Version 2.
        var updateResponse = await PutProfileAsync(client, tenantId, ValidProfileBody(
            "Renamed Boutique", instagramUrl: "https://www.instagram.com/firstboutique", expectedVersion: 1));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ShopProfileEnvelopeDto>();
        Assert.NotNull(updated?.Profile);
        Assert.Equal("Renamed Boutique", updated!.Profile!.Name);
        Assert.Equal("https://www.instagram.com/firstboutique", updated.Profile.InstagramUrl);
        Assert.Equal(2, updated.Profile.Version);

        // Reload: the same values persist, and the database row agrees.
        var reloaded = await GetAdminProfileAsync(client, tenantId);
        Assert.NotNull(reloaded);
        Assert.Equal("Renamed Boutique", reloaded!.Name);
        Assert.Equal(2, reloaded.Version);
        Assert.Equal(created.Profile.Id, reloaded.Id);

        var stored = await GetStoredProfileAsync(tenantId);
        Assert.NotNull(stored);
        Assert.Equal("Renamed Boutique", stored!.Value.Name);
        Assert.Equal(2, stored.Value.Version);
        Assert.Equal("https://www.instagram.com/firstboutique", stored.Value.InstagramUrl);
        Assert.True(stored.Value.IsPublished);
    }

    [Fact]
    public async Task AdminPut_StaleExpectedVersion_Returns409StaleVersion_AndChangesNothing()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();
        var createResponse = await PutProfileAsync(client, tenantId, ValidProfileBody("Original", expectedVersion: null));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        // A version that never existed is stale.
        var staleResponse = await PutProfileAsync(client, tenantId, ValidProfileBody("Stale Writer", expectedVersion: 0));
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        using var problem = JsonDocument.Parse(await staleResponse.Content.ReadAsStringAsync());
        Assert.Equal("stale_version", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());

        // The winner's row is untouched.
        var reloaded = await GetAdminProfileAsync(client, tenantId);
        Assert.NotNull(reloaded);
        Assert.Equal("Original", reloaded!.Name);
        Assert.Equal(1, reloaded.Version);
        Assert.Equal(1, await CountProfilesForTenantAsync(tenantId));
    }

    [Fact]
    public async Task TwoConcurrentCreates_ExactlyOneSucceeds_OtherIs409_AndOneRowExists()
    {
        var (tenantId, ownerClientA, ownerAccount) = await NewTenantWithOwnerAsync();
        using var clientB = CreateClient();
        SetMemberToken(clientB, ownerAccount, $"ownerB-{ownerAccount.ToLong():x}@tenantforge.local");

        // Two genuinely parallel first-saves for the same tenant.
        var results = await Task.WhenAll(
            PutProfileAsync(ownerClientA, tenantId, ValidProfileBody("Winner A", expectedVersion: null)),
            PutProfileAsync(clientB, tenantId, ValidProfileBody("Winner B", expectedVersion: null)));
        var first = results[0];
        var second = results[1];

        var statuses = new[] { first.StatusCode, second.StatusCode }.OrderBy(status => (int)status).ToArray();
        Assert.Equal(HttpStatusCode.OK, statuses[0]);
        Assert.Equal(HttpStatusCode.Conflict, statuses[1]);

        var loser = first.StatusCode == HttpStatusCode.Conflict ? first : second;
        using var problem = JsonDocument.Parse(await loser.Content.ReadAsStringAsync());
        Assert.Equal("stale_version", problem.RootElement.GetProperty("type").GetString());

        var winner = first.StatusCode == HttpStatusCode.OK ? first : second;
        var winnerProfile = (await winner.Content.ReadFromJsonAsync<ShopProfileEnvelopeDto>())!.Profile;
        Assert.NotNull(winnerProfile);
        Assert.Equal(1, winnerProfile.Version);

        // Exactly one row survived the race, owned by this tenant.
        Assert.Equal(1, await CountProfilesForTenantAsync(tenantId));
        var stored = await GetStoredProfileAsync(tenantId);
        Assert.NotNull(stored);
        Assert.Equal(winnerProfile!.Name, stored!.Value.Name);
    }

    [Fact]
    public async Task TenantIsolation_A_CannotReadOrWrite_BsProfile()
    {
        var (tenantA, clientA, _) = await NewTenantWithOwnerAsync();
        var (tenantB, clientB, _) = await NewTenantWithOwnerAsync();

        // Tenant B publishes a profile first.
        var createB = await PutProfileAsync(clientB, tenantB, ValidProfileBody("Boutique B", expectedVersion: null));
        Assert.Equal(HttpStatusCode.OK, createB.StatusCode);

        // A reads its own (empty) state — never B's row.
        var readAsA = await GetAdminProfileAsync(clientA, tenantA);
        Assert.Null(readAsA);

        // A writes; the server scopes the write to A from the authenticated
        // route context, so B's row must be untouched.
        var writeAsA = await PutProfileAsync(clientA, tenantA, ValidProfileBody("Boutique A", expectedVersion: null));
        Assert.Equal(HttpStatusCode.OK, writeAsA.StatusCode);
        var profileA = (await writeAsA.Content.ReadFromJsonAsync<ShopProfileEnvelopeDto>())!.Profile;
        Assert.NotNull(profileA);
        Assert.Equal(tenantA, profileA!.TenantId);

        // B still sees its own row, unchanged (name, version).
        var reloadedB = await GetAdminProfileAsync(clientB, tenantB);
        Assert.NotNull(reloadedB);
        Assert.Equal("Boutique B", reloadedB!.Name);
        Assert.Equal(1, reloadedB.Version);

        // One row per tenant, never shared.
        Assert.Equal(1, await CountProfilesForTenantAsync(tenantA));
        Assert.Equal(1, await CountProfilesForTenantAsync(tenantB));
    }

    [Fact]
    public async Task Put_WithoutSettingsManagePermission_IsRejected_AndRoleGrantOrOwnerSucceeds()
    {
        var (tenantId, ownerClient, _) = await NewTenantWithOwnerAsync();
        var memberAccount = await CreateMemberAccountAsync(tenantId, $"member-{Guid.NewGuid():N}@tenantforge.local");
        using var memberClient = CreateClient();
        SetMemberToken(memberClient, memberAccount, $"member-{memberAccount.ToLong():x}@tenantforge.local");

        // A plain member (no role grant) is denied the save — 403.
        var denied = await PutProfileAsync(memberClient, tenantId, ValidProfileBody("Nope", expectedVersion: null));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Read-only access stays membership-only: the same member can read.
        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync($"/api/tenants/{tenantId}/shop/profile")).StatusCode);

        // A role carrying exactly Shop.Settings.Manage unlocks the save.
        await GrantRoleAsync(tenantId, memberAccount, ["Shop.Settings.Manage"]);
        var granted = await PutProfileAsync(memberClient, tenantId, ValidProfileBody("Granted Boutique", expectedVersion: null));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal("Granted Boutique", (await granted.Content.ReadFromJsonAsync<ShopProfileEnvelopeDto>())!.Profile!.Name);

        // The Owner bypass is the happy path of every other test above, but
        // pin it explicitly: the owner (no role grant at all) can still save.
        var ownerSave = await PutProfileAsync(ownerClient, tenantId, ValidProfileBody(
            "Owner Boutique", expectedVersion: (await GetAdminProfileAsync(ownerClient, tenantId))!.Version));
        Assert.Equal(HttpStatusCode.OK, ownerSave.StatusCode);
        Assert.Equal("Owner Boutique", (await ownerSave.Content.ReadFromJsonAsync<ShopProfileEnvelopeDto>())!.Profile!.Name);
    }

    [Fact]
    public async Task Put_TrimsOutsideOnly_PreservesInternalNewlines_AndRejectsOverLength()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();
        var aboutText = "  Line one\nLine two\n  ";

        var response = await PutProfileAsync(client, tenantId, new
        {
            name = "  Trimmed Boutique  ",
            tagline = "  Trimmed tagline  ",
            supportPhone = "  021 8877 6655  ",
            instagramUrl = (string?)null,
            aboutText,
            shippingPolicy = "  Shipping text  ",
            paymentPolicy = "Payment text",
            returnPolicy = "Return text",
            privacyPolicy = "Privacy text",
            isPublished = true,
            expectedVersion = (int?)null
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = (await response.Content.ReadFromJsonAsync<ShopProfileEnvelopeDto>())!.Profile;
        Assert.NotNull(profile);
        Assert.Equal("Trimmed Boutique", profile!.Name);
        Assert.Equal("Trimmed tagline", profile.Tagline);
        Assert.Equal("021 8877 6655", profile.SupportPhone);

        // The persisted row trimmed the outside only — the internal newline
        // in about_text is preserved exactly as typed.
        var stored = await GetStoredProfileAsync(tenantId);
        Assert.NotNull(stored);
        Assert.Equal("Line one\nLine two", stored!.Value.AboutText);
        Assert.Equal("Trimmed Boutique", stored.Value.Name);

        // A field over its maximum is a 400 naming that field.
        var tooLong = await PutProfileAsync(client, tenantId, ValidProfileBody(new string('n', 101), expectedVersion: profile.Version));
        var tooLongBody = await tooLong.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        using var problem = JsonDocument.Parse(tooLongBody);
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("name", out _));

        // And the stored row still holds the previous value.
        Assert.Equal("Trimmed Boutique", (await GetStoredProfileAsync(tenantId))!.Value.Name);
    }

    [Fact]
    public async Task Put_InstagramUrl_NotHttpsOrWrongHost_ReturnsValidationError()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();

        async Task AssertInstagramErrorAsync(string url)
        {
            var response = await PutProfileAsync(client, tenantId, ValidProfileBody("Boutique", instagramUrl: url, expectedVersion: null));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("instagramUrl", out _));
        }

        await AssertInstagramErrorAsync("http://instagram.com");
        await AssertInstagramErrorAsync("https://instagram.com.evil.example");
        await AssertInstagramErrorAsync("https://notinstagram.com");
        await AssertInstagramErrorAsync("not a url at all");

        // Nothing persisted.
        Assert.Equal(0, await CountProfilesForTenantAsync(tenantId));
    }

    [Fact]
    public async Task Put_SupportPhone_DisallowedCharacters_ReturnsValidationError()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();

        async Task SubmitAsync(string phone)
        {
            var response = await PutProfileAsync(client, tenantId, new
            {
                name = "Boutique",
                tagline = "Tagline",
                supportPhone = phone,
                instagramUrl = (string?)null,
                aboutText = "About",
                shippingPolicy = "Shipping",
                paymentPolicy = "Payment",
                returnPolicy = "Return",
                privacyPolicy = "Privacy",
                isPublished = true,
                expectedVersion = (int?)null
            });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("supportPhone", out _));
        }

        await SubmitAsync("021-8877-6655x");   // a letter
        await SubmitAsync("021 8877 6655,");   // a comma
        await SubmitAsync(new string('p', 31)); // over 30 characters

        // The allowlist accepts the documented display shapes.
        var ok = await PutProfileAsync(client, tenantId, new
        {
            name = "Boutique",
            tagline = "Tagline",
            supportPhone = "+98 21 8877-6655",
            instagramUrl = (string?)null,
            aboutText = "About",
            shippingPolicy = "Shipping",
            paymentPolicy = "Payment",
            returnPolicy = "Return",
            privacyPolicy = "Privacy",
            isPublished = true,
            expectedVersion = (int?)null
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task PublicGet_UnpublishedOrMissing_Returns404()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();
        using var anonymous = CreateClient();

        // No profile at all: 404.
        var missing = await anonymous.GetAsync($"/api/shop/{tenantId}/profile");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // An existing but unpublished profile: the same 404.
        var unpublished = await PutProfileAsync(client, tenantId, ValidProfileBody("Hidden Boutique", expectedVersion: null, isPublished: false));
        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);

        var stillHidden = await anonymous.GetAsync($"/api/shop/{tenantId}/profile");
        Assert.Equal(HttpStatusCode.NotFound, stillHidden.StatusCode);

        // The admin can still see the unpublished draft.
        var draft = await GetAdminProfileAsync(client, tenantId);
        Assert.NotNull(draft);
        Assert.False(draft!.IsPublished);

        // A malformed tenant id is the same non-leaking 404.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/shop/not-a-tsid/profile")).StatusCode);
    }

    [Fact]
    public async Task PublicGet_PublishedProfile_ReturnsPublicShape_WithoutVersion()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();
        var created = await PutProfileAsync(client, tenantId, ValidProfileBody(
            "Published Boutique", instagramUrl: "https://instagram.com/published", expectedVersion: null));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using var anonymous = CreateClient(); // no Authorization header at all
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var publicProfile = JsonSerializer.Deserialize<PublicShopProfileDto>(body, Json);
        Assert.NotNull(publicProfile);
        Assert.Equal("Published Boutique", publicProfile!.Name);
        Assert.Equal("https://instagram.com/published", publicProfile.InstagramUrl);
        Assert.Equal("A small curated shop", publicProfile.Tagline);
        Assert.Equal("021 8877 6655", publicProfile.SupportPhone);
        Assert.Equal("We ship everywhere", publicProfile.ShippingPolicy);
        Assert.True(publicProfile.IsPublished);

        // The public wire shape carries no internal members at all — the
        // concurrency counter and every id are admin-only.
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.TryGetProperty("version", out _));
        Assert.False(document.RootElement.TryGetProperty("id", out _));
        Assert.False(document.RootElement.TryGetProperty("tenantId", out _));
        Assert.False(document.RootElement.TryGetProperty("updatedAtUtc", out _));
    }

    [Fact]
    public async Task PublicationDoesNotGateTheStorefrontCatalog()
    {
        // Spec step 8: IsPublished controls only the profile/policy pages.
        // With no profile at all, the public storefront reads still work.
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();
        using var anonymous = CreateClient();

        var category = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name = "Shirts",
            slug = $"shirts-{Guid.NewGuid():N}"[..16],
            displayOrder = 1
        });
        Assert.Equal(HttpStatusCode.Created, category.StatusCode);

        var publicCategories = await anonymous.GetAsync($"/api/shop/{tenantId}/categories");
        Assert.Equal(HttpStatusCode.OK, publicCategories.StatusCode);
        var list = await publicCategories.Content.ReadFromJsonAsync<PublicCategoryListDto>();
        Assert.NotNull(list);
        Assert.Single(list!.Categories);
    }
}

internal sealed record ShopProfileDto(
    string Id, string TenantId, string Name, string Tagline,
    string SupportPhone, string? InstagramUrl, string AboutText,
    string ShippingPolicy, string PaymentPolicy, string ReturnPolicy,
    string PrivacyPolicy, bool IsPublished, int Version, DateTimeOffset UpdatedAtUtc);

internal sealed record ShopProfileEnvelopeDto(ShopProfileDto? Profile);

internal sealed record PublicShopProfileDto(
    string Name, string Tagline, string SupportPhone, string? InstagramUrl,
    string AboutText, string ShippingPolicy, string PaymentPolicy,
    string ReturnPolicy, string PrivacyPolicy, bool IsPublished);

internal sealed record PublicCategoryListDto(IReadOnlyList<PublicCategoryDto> Categories);

internal sealed record PublicCategoryDto(
    string Id, string Name, string Slug, int DisplayOrder,
    IReadOnlyList<PublicCategoryDto> Children);
