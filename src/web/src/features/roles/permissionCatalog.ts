import type { PermissionGroup, PermissionKey } from './roleTypes'

export const PERMISSION_CATALOG: PermissionGroup[] = [
  {
    id: 'dashboard',
    label: 'داشبورد',
    description: 'دسترسی خواندن به نمای خلاصه وضعیت مستأجر.',
    permissions: [
      {
        key: 'IAM.Dashboard.View',
        label: 'مشاهده داشبورد',
        description: 'اجازه دیدن خلاصه‌ها و شاخص‌های صفحه داشبورد.',
        kind: 'read',
      },
    ],
  },
  {
    id: 'users',
    label: 'کاربران',
    description: 'مجوزهای لازم برای کار با صفحه کاربران پلتفرم در همین محدوده.',
    permissions: [
      {
        key: 'IAM.Users.View',
        label: 'مشاهده کاربران',
        description: 'اجازه دیدن فهرست کاربران و وضعیت حساب‌ها.',
        kind: 'read',
      },
      {
        key: 'IAM.Users.Create',
        label: 'ایجاد کاربر',
        description: 'اجازه شروع عملیات ایجاد حساب کاربری جدید.',
        kind: 'write',
      },
    ],
  },
  {
    id: 'tenants',
    label: 'مستأجران',
    description: 'مجوزهای لازم برای مشاهده و ایجاد مستأجر در صفحه مدیریت.',
    permissions: [
      {
        key: 'IAM.Tenants.View',
        label: 'مشاهده مستأجران',
        description: 'اجازه دیدن فهرست مستأجران قابل مدیریت.',
        kind: 'read',
      },
      {
        key: 'IAM.Tenants.Create',
        label: 'ایجاد مستأجر',
        description: 'اجازه ایجاد مستأجر جدید با مالک نخست.',
        kind: 'write',
      },
    ],
  },
]

export const PERMISSION_KEYS = PERMISSION_CATALOG.flatMap((group) =>
  group.permissions.map((permission) => permission.key),
)

const keySet = new Set<PermissionKey>(PERMISSION_KEYS)

export function isPermissionKey(value: unknown): value is PermissionKey {
  return typeof value === 'string' && keySet.has(value as PermissionKey)
}
