using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Contract.Requests;
using TenantForge.Modules.Iam.Contract.Responses;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Features.Pagination;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.Tenants;

internal static partial class TenantsFeature
{
    public static IEndpointRouteBuilder MapTenantsFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/platform/tenants", async (HttpRequest request, IamDbContext db) =>
        {
            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            var query = db.Tenants
                .AsNoTracking()
                .GroupJoin(
                    db.TenantMemberships.AsNoTracking(),
                    tenant => tenant.Id,
                    membership => membership.TenantId,
                    (tenant, memberships) => new
                    {
                        tenant.Id,
                        tenant.Name,
                        tenant.Slug,
                        tenant.Status,
                        MemberCount = memberships.Count(),
                        tenant.CreatedAtUtc
                    })
                .OrderBy(tenant => tenant.CreatedAtUtc)
                .ThenBy(tenant => tenant.Id);

            var (tenantRows, pagination) = await PaginationSupport.PageAsync(query, page);

            var tenants = tenantRows.Select(tenant => new TenantSummaryResponse(
                TsidId.Format(tenant.Id),
                tenant.Name,
                tenant.Slug,
                tenant.Status.ToString(),
                tenant.MemberCount,
                tenant.CreatedAtUtc.UtcDateTime.ToString("O")))
                .ToList();

            return Results.Ok(new TenantListResponse(tenants, pagination));
        })
        .RequireAuthorization(AuthorizationPolicyNames.PlatformAdmin);

        endpoints.MapPost("/api/platform/tenants", async (
            CreateTenantRequest request,
            [FromServices] IamDbContext db) =>
        {
            var validationErrors = Validate(request);
            if (validationErrors.Count > 0)
            {
                return Results.ValidationProblem(validationErrors);
            }

            var normalizedSlug = Tenant.NormalizeSlug(request.Slug);
            var duplicateExists = await db.Tenants.AnyAsync(tenant => tenant.NormalizedSlug == normalizedSlug);
            if (duplicateExists)
            {
                return DuplicateSlugProblem();
            }

            // Validate() already ran TsidId.TryParse on ownerUserId; the null
            // coalesce is unreachable in the normal flow and, if it ever were
            // hit, simply falls through to the same "Select an existing active
            // owner" 400 rather than throwing.
            var ownerAccountId = TsidId.TryParse(request.OwnerUserId, out var parsedOwner) ? parsedOwner : default;
            var ownerExists = await db.Accounts.AnyAsync(account =>
                account.Id == ownerAccountId && account.Status == AccountStatus.Active);
            if (!ownerExists)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ownerUserId"] = ["Select an existing active owner."]
                });
            }

            var now = DateTimeOffset.UtcNow;
            var tenant = Tenant.Create(request.Name!, normalizedSlug, now);
            var ownerMembership = TenantMembership.CreateOwner(tenant.Id, ownerAccountId, now);

            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Tenants.Add(tenant);
            db.TenantMemberships.Add(ownerMembership);

            try
            {
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                return DuplicateSlugProblem();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            var response = new TenantSummaryResponse(
                TsidId.Format(tenant.Id),
                tenant.Name,
                tenant.Slug,
                tenant.Status.ToString(),
                MemberCount: 1,
                tenant.CreatedAtUtc.UtcDateTime.ToString("O"));

            return Results.Created($"/api/platform/tenants/{TsidId.Format(tenant.Id)}", response);
        })
        .RequireAuthorization(AuthorizationPolicyNames.PlatformAdmin);

        return endpoints;
    }

    private static Dictionary<string, string[]> Validate(CreateTenantRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Tenant name is required."];
        }
        else if (name.Length > 80)
        {
            errors["name"] = ["Tenant name must be 80 characters or fewer."];
        }

        var normalizedSlug = Tenant.NormalizeSlug(request.Slug);
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            errors["slug"] = ["Enter a valid tenant slug using a-z, 0-9 and dashes."];
        }
        else if (normalizedSlug.Length < 3)
        {
            errors["slug"] = ["Tenant slug must be at least 3 characters."];
        }
        else if (normalizedSlug.Length > 50)
        {
            errors["slug"] = ["Tenant slug must be 50 characters or fewer."];
        }
        else if (!SlugPattern().IsMatch(normalizedSlug))
        {
            errors["slug"] = ["Tenant slug must start and end with a letter or digit."];
        }

        if (string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            errors["ownerUserId"] = ["Select the first tenant owner."];
        }
        // B017/S19: owner input must be a canonical TSID string. TsidId.TryParse
        // already rejects GUID-shaped and decimal input, so a GUID owner id now
        // fails as "Select a valid owner user." (400) — the same validation
        // failure the old shape produced, without weakening the check.
        else if (!TsidId.TryParse(request.OwnerUserId, out _))
        {
            errors["ownerUserId"] = ["Select a valid owner user."];
        }

        return errors;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult DuplicateSlugProblem() => Results.Problem(
        title: "Duplicate tenant slug",
        detail: "A tenant with this slug already exists.",
        statusCode: StatusCodes.Status409Conflict);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugPattern();
}
