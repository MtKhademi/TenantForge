using TenantForge.BuildingBlocks.Permissions;

namespace TenantForge.Modules.Iam.Features.Roles;

/// <summary>
/// IAM's own contribution to the shared permission catalog (B034). The
/// exact same 3 groups RolesFeature used to hardcode as
/// Contract-typed PermissionGroupResponse values — same ids, labels,
/// descriptions, keys, kinds — now expressed in the cross-module shape.
/// </summary>
internal sealed class IamPermissionCatalogContributor : IPermissionCatalogContributor
{
    public IReadOnlyList<PermissionGroup> GetPermissionGroups() =>
    [
        new("roles", "نقش‌ها", "مدیریت نقش‌ها و مجوزهای مستأجر.",
        [
            new(RolesFeature.RolesManagePermission, "مدیریت نقش‌ها", "اجازه ایجاد، ویرایش و تخصیص نقش‌های مستأجر.", "write")
        ]),
        new("invitations", "دعوت‌ها", "مدیریت دعوت‌نامه‌های مستأجر.",
        [
            new(RolesFeature.InvitationsViewPermission, "مشاهده دعوت‌ها", "اجازه دیدن دعوت‌نامه‌های در انتظار.", "read"),
            new(RolesFeature.InvitationsCreatePermission, "ایجاد دعوت", "اجازه ایجاد دعوت‌نامه جدید برای مستأجر.", "write")
        ]),
        new("audit", "گزارش فعالیت", "دسترسی به رویدادهای ثبت‌شده مستأجر.",
        [
            new(RolesFeature.AuditViewPermission, "مشاهده گزارش فعالیت", "اجازه خواندن گزارش فعالیت مستأجر.", "read")
        ])
    ];
}
