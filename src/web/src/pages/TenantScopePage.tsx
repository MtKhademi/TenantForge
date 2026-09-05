import { Building2, ChevronRight, Loader2, UserRound, Users } from 'lucide-react'
import { useParams } from 'react-router-dom'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { SecondaryButton } from '@/components/ui/Button'
import { useTenantScope } from '@/features/tenants/TenantScopeContext'
import { cn } from '@/lib/utils'

/**
 * S07 tenant-scoped shell (F010, mocked).
 *
 * A deliberately thin workspace that makes the **tenant context obvious**:
 * the header switcher shows the tenant, and this page shows the tenant's
 * name, slug, status and member count, plus a back-to-platform action. No
 * business data is rendered yet — tenant-specific screens are later slices.
 *
 * The URL slug is the single source of truth for the active tenant, derived
 * here with `useParams` (no duplicated "active tenant" state). If the slug is
 * unknown once the list has settled, the page says so and offers the platform
 * view — selecting a tenant only changes presentation, it never grants access.
 */
export function TenantScopePage() {
  const { slug } = useParams<{ slug: string }>()
  const { tenants, isBusy, failure, refresh, selectPlatform, getTenantBySlug } = useTenantScope()

  const tenant = slug ? getTenantBySlug(slug) : null
  const listSettled = tenants !== null
  const isLoading = isBusy && !listSettled

  return (
    <DashboardShell>
      <section aria-label={`محدوده مستأجر ${tenant?.name ?? ''}`} className="space-y-6">
        <button
          type="button"
          onClick={selectPlatform}
          className="inline-flex min-h-9 items-center gap-1.5 rounded-md border border-border bg-surface px-3 text-sm font-medium transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          <ChevronRight aria-hidden="true" className="size-4" />
          بازگشت به پلتفرم
        </button>

        {isLoading && (
          <div
            className="flex items-center gap-3 rounded-xl border border-border bg-surface p-6 shadow-soft"
            aria-busy="true"
          >
            <Loader2
              aria-hidden="true"
              className="size-5 animate-spin text-muted-foreground motion-reduce:animate-none"
            />
            <p className="text-sm font-medium text-muted-foreground">
              در حال بارگذاری محدوده مستأجر…
            </p>
          </div>
        )}

        {!isLoading && listSettled && failure !== null && (
          <div className="rounded-xl border border-border bg-surface p-6 shadow-soft" role="alert">
            <p className="text-sm font-semibold">فهرست مستأجران در دسترس نیست</p>
            <p className="mt-1 text-sm leading-6 text-muted-foreground">
              نمی‌توانیم این محدوده را بارگذاری کنیم. دوباره تلاش کنید.
            </p>
            <SecondaryButton type="button" className="mt-4" onClick={() => refresh()}>
              تلاش دوباره
            </SecondaryButton>
          </div>
        )}

        {!isLoading && listSettled && failure === null && tenant === null && (
          <div className="rounded-xl border border-border bg-surface p-6 shadow-soft" role="alert">
            <p className="text-sm font-semibold">مستأجر یافت نشد</p>
            <p className="mt-1 text-sm leading-6 text-muted-foreground">
              این محدوده در فهرست مستأجران موجود نیست.
            </p>
            <SecondaryButton type="button" className="mt-4" onClick={selectPlatform}>
              بازگشت به پلتفرم
            </SecondaryButton>
          </div>
        )}

        {!isLoading && listSettled && failure === null && tenant !== null && (
          <div className="space-y-4">
            <div className="flex items-center gap-4 rounded-xl border border-primary/30 bg-primary/5 p-5 shadow-soft">
              <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-primary/15 text-primary">
                <Building2 aria-hidden="true" className="size-6" />
              </span>
              <div className="min-w-0">
                <p className="text-xs font-semibold tracking-[0.08em] text-primary">
                  محدوده مستأجر
                </p>
                <h2 className="mt-1 truncate text-xl font-semibold tracking-tight md:text-2xl">
                  <bdi>{tenant.name}</bdi>
                </h2>
                <p className="mt-0.5 text-xs text-muted-foreground">
                  <code dir="ltr" className="rounded bg-muted px-1.5 py-0.5">
                    {tenant.slug}
                  </code>
                </p>
              </div>
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              <div className="rounded-xl border border-border bg-surface p-4 shadow-soft">
                <p className="flex items-center gap-2 text-xs font-semibold text-muted-foreground">
                  <Users aria-hidden="true" className="size-3.5" />
                  تعداد اعضا
                </p>
                <p className="mt-2 text-lg font-semibold">
                  <bdi>{tenant.memberCount}</bdi>
                  <span className="ms-2 text-xs font-normal text-muted-foreground">
                    عضویت (مالک نخست)
                  </span>
                </p>
              </div>
              <div className="rounded-xl border border-border bg-surface p-4 shadow-soft">
                <p className="flex items-center gap-2 text-xs font-semibold text-muted-foreground">
                  <UserRound aria-hidden="true" className="size-3.5" />
                  وضعیت
                </p>
                <p className="mt-2 text-lg font-semibold">
                  {tenant.status === 'Active' ? 'فعال' : 'معلول'}
                </p>
              </div>
            </div>

            <p
              className={cn(
                'rounded-lg border border-border bg-surface p-4 text-sm leading-6 text-muted-foreground shadow-soft',
              )}
            >
              این صفحه نمونهٔ محدودهٔ مستأجر است؛ صفحات کسب‌وکار اختصاصی مستأجر در برش‌های بعدی
              اضافه می‌شوند. انتخاب این محدوده تنها نمایش را تغییر می‌دهد و هرگز دسترسی نمی‌بخشد.
            </p>
          </div>
        )}
      </section>
    </DashboardShell>
  )
}
