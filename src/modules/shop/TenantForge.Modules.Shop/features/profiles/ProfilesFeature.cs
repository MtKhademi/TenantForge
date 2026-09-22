using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Authorization;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Profiles;

internal static class ProfilesFeature
{
    public static IEndpointRouteBuilder MapProfilesFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenants/{tenantId}/shop/profile", async (
            string tenantId,
            ClaimsPrincipal principal,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            var profile = await db.Profiles.AsNoTracking()
                .SingleOrDefaultAsync(profile => profile.TenantId == access.TenantId, ct);
            return Results.Ok(new ShopProfileResponse(profile is null ? null : ToDto(profile)));
        }).RequireAuthorization();

        endpoints.MapPut("/api/tenants/{tenantId}/shop/profile", async (
            string tenantId,
            SaveShopProfileRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.SettingsManagePermission);
            if (access.Result is not null) return access.Result;

            var errors = ValidateProfileFields(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var nowUtc = clock.GetUtcNow();
            ShopProfile? profile;

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // Load the tenant's single row, if any, locked FOR UPDATE so a
                // concurrent update is serialized against ours.
                profile = await LockProfileForTenantAsync(db, access.TenantId, ct);

                if (request.ExpectedVersion is null)
                {
                    // Create. If a row already exists, this is a stale create —
                    // the spec routes it to the same 409 as a stale update.
                    if (profile is not null) return StaleVersionProblem();

                    profile = ShopProfile.Create(
                        access.TenantId,
                        request.Name!, request.Tagline!, request.SupportPhone!, request.InstagramUrl,
                        request.AboutText!, request.ShippingPolicy!, request.PaymentPolicy!,
                        request.ReturnPolicy!, request.PrivacyPolicy!,
                        request.IsPublished, nowUtc);
                    db.Profiles.Add(profile);
                }
                else
                {
                    if (profile is null || profile.Version != request.ExpectedVersion)
                    {
                        return StaleVersionProblem();
                    }

                    profile.Update(
                        request.Name!, request.Tagline!, request.SupportPhone!, request.InstagramUrl,
                        request.AboutText!, request.ShippingPolicy!, request.PaymentPolicy!,
                        request.ReturnPolicy!, request.PrivacyPolicy!,
                        request.IsPublished, nowUtc);
                }

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                {
                    // The unique tenant_id constraint caught two racing first
                    // creates. The winner committed; this loser must not
                    // propagate its exception past the open transaction, so
                    // roll back explicitly and surface the conflict.
                    await transaction.RollbackAsync(CancellationToken.None);
                    return StaleVersionProblem();
                }

                await transaction.CommitAsync(ct);
                return Results.Ok(new ShopProfileResponse(ToDto(profile)));
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }).RequireAuthorization();

        endpoints.MapGet("/api/shop/{tenantId}/profile", async (
            string tenantId,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            // Public: no profile or an unpublished one are the same 404 — the
            // storefront renders a neutral fallback for both.
            var profile = await db.Profiles.AsNoTracking()
                .SingleOrDefaultAsync(profile => profile.TenantId == tenantTsid && profile.IsPublished, ct);
            if (profile is null) return Results.NotFound();

            return Results.Ok(ToPublicDto(profile));
        });

        return endpoints;
    }

    /// <summary>
    /// B039: acquires a PostgreSQL row lock (SELECT ... FOR UPDATE) on the
    /// tenant's single profile row, or null when none exists, so a concurrent
    /// create/update is serialized against ours. Uses the raw bigint values
    /// because the lock must be expressed in SQL, not LINQ — the same pattern
    /// B038 uses on shop_categories.
    /// </summary>
    private static Task<ShopProfile?> LockProfileForTenantAsync(ShopDbContext db, Tsid tenantId, CancellationToken ct) =>
        db.Profiles
            .FromSqlRaw(
                "SELECT * FROM shop_profiles WHERE tenant_id = {0} FOR UPDATE",
                tenantId.ToLong())
            .SingleOrDefaultAsync(ct);

    private static IResult StaleVersionProblem() => Results.Problem(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Type = "stale_version",
        Title = "Profile version conflict",
        Detail = "The profile was changed before this request was applied. Reload and try again."
    });

    /// <summary>
    /// B039: trims nothing here (the domain trims on save) but enforces every
    /// length, phone-character and Instagram-URL rule. A blank required field
    /// is a validation error; optional long texts may be empty.
    /// </summary>
    private static Dictionary<string, string[]> ValidateProfileFields(SaveShopProfileRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["Store name is required."];
        else if (request.Name.Trim().Length > 100) errors["name"] = ["Store name must be 100 characters or fewer."];

        if (string.IsNullOrWhiteSpace(request.Tagline)) errors["tagline"] = ["Tagline is required."];
        else if (request.Tagline.Trim().Length > 180) errors["tagline"] = ["Tagline must be 180 characters or fewer."];

        if (string.IsNullOrWhiteSpace(request.SupportPhone))
        {
            errors["supportPhone"] = ["Support phone is required."];
        }
        else
        {
            var phone = request.SupportPhone.Trim();
            if (phone.Length > 30 || !IsValidDisplayPhone(phone))
            {
                errors["supportPhone"] = ["Support phone may contain digits, spaces, '+', '-' and parentheses only."];
            }
        }

        if (!string.IsNullOrWhiteSpace(request.InstagramUrl))
        {
            var url = request.InstagramUrl.Trim();
            if (url.Length > 300 || !IsInstagramUrl(url))
            {
                errors["instagramUrl"] = ["Instagram URL must be an HTTPS URL on instagram.com or a subdomain of it."];
            }
        }

        if (!string.IsNullOrWhiteSpace(request.AboutText) && request.AboutText.Trim().Length > 4000)
            errors["aboutText"] = ["About text must be 4000 characters or fewer."];

        if (!string.IsNullOrWhiteSpace(request.ShippingPolicy) && request.ShippingPolicy.Trim().Length > 6000)
            errors["shippingPolicy"] = ["Shipping policy must be 6000 characters or fewer."];

        if (!string.IsNullOrWhiteSpace(request.PaymentPolicy) && request.PaymentPolicy.Trim().Length > 6000)
            errors["paymentPolicy"] = ["Payment policy must be 6000 characters or fewer."];

        if (!string.IsNullOrWhiteSpace(request.ReturnPolicy) && request.ReturnPolicy.Trim().Length > 6000)
            errors["returnPolicy"] = ["Return policy must be 6000 characters or fewer."];

        if (!string.IsNullOrWhiteSpace(request.PrivacyPolicy) && request.PrivacyPolicy.Trim().Length > 6000)
            errors["privacyPolicy"] = ["Privacy policy must be 6000 characters or fewer."];

        return errors;
    }

    /// <summary>
    /// B039: the conservative phone allowlist — a display phone is any
    /// non-empty string of digits, spaces, '+', '-' and parentheses. This is
    /// a character-class check only (per the spec: "a conservative allowlist
    /// of characters"), so "0912 345 6789", "+98 21 88 77 66" and
    /// "(021)8877" all pass while any other character (letters, punctuation,
    /// non-ASCII) fails.
    /// </summary>
    private static bool IsValidDisplayPhone(string phone)
    {
        foreach (var ch in phone)
        {
            if (!char.IsDigit(ch) && ch != ' ' && ch != '+' && ch != '-' && ch != '(' && ch != ')')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// B039: an HTTPS URL whose host is exactly <c>instagram.com</c> or a
    /// subdomain of it. <c>instagram.com.evil.example</c> must never match:
    /// the suffix check anchors on the dot, so a host that merely *ends with*
    /// the string is rejected, and a host containing a dot is accepted only
    /// when the segment before the last dot chain is itself a label of
    /// instagram.com.
    /// </summary>
    private static bool IsInstagramUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host; // lower-cased by the Uri parser
        const string exact = "instagram.com";
        const string suffix = ".instagram.com";
        return host == exact || (host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static ShopProfileDto ToDto(ShopProfile profile) => new(
        TsidId.Format(profile.Id),
        TsidId.Format(profile.TenantId),
        profile.Name,
        profile.Tagline,
        profile.SupportPhone,
        profile.InstagramUrl,
        profile.AboutText,
        profile.ShippingPolicy,
        profile.PaymentPolicy,
        profile.ReturnPolicy,
        profile.PrivacyPolicy,
        profile.IsPublished,
        profile.Version,
        profile.UpdatedAtUtc);

    private static PublicShopProfileResponse ToPublicDto(ShopProfile profile) => new(
        profile.Name,
        profile.Tagline,
        profile.SupportPhone,
        profile.InstagramUrl,
        profile.AboutText,
        profile.ShippingPolicy,
        profile.PaymentPolicy,
        profile.ReturnPolicy,
        profile.PrivacyPolicy,
        profile.IsPublished);
}
