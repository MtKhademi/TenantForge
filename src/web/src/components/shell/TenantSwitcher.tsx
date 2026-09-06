import { Building2, Check, ChevronsUpDown, Loader2, ShieldCheck } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useLocation, useParams } from 'react-router-dom'
import { useTenantScope } from '@/features/tenants/TenantScopeContext'
import { cn } from '@/lib/utils'

/**
 * S07 tenant switcher (F010) — S08: selection is by tenant id.
 *
 * The trigger shows the active scope: «پلتفرم» on platform routes, or the
 * tenant name when a `/t/:tenantId` route is active. When the route is a
 * tenant the signed-in user cannot see in the platform list (e.g. a member
 * whose tenant list fetch is denied), the trigger shows a neutral «مستأجر»
 * rather than pretending the scope is the platform. The menu lists «پلتفرم»
 * plus every known tenant. Choosing an entry only navigates (selection,
 * never authorization — the server decides access in S08).
 *
 * Behavior contract:
 * - the trigger carries `aria-haspopup="listbox"` + `aria-expanded`;
 * - the menu is a `listbox` of `option`s with `aria-selected`;
 * - Escape closes and returns focus to the trigger; clicking the backdrop
 *   closes; ArrowUp/ArrowDown move focus across the options;
 * - while the tenant list is still loading the menu shows one busy row.
 */
export function TenantSwitcher() {
  const { tenants, isBusy, selectTenant, selectPlatform } = useTenantScope()
  const { tenantId: activeTenantId } = useParams<{ tenantId: string }>()
  const location = useLocation()
  const onTenantRoute = location.pathname.startsWith('/t/')
  const activeTenant =
    activeTenantId && onTenantRoute
      ? (tenants?.find((tenant) => tenant.id === activeTenantId) ?? null)
      : null

  const [open, setOpen] = useState(false)
  const triggerRef = useRef<HTMLButtonElement | null>(null)
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([])

  const options = [
    { key: 'platform', label: 'پلتفرم', tenantId: null as string | null, icon: ShieldCheck },
    ...(tenants ?? []).map((tenant) => ({
      key: tenant.id,
      label: tenant.name,
      tenantId: tenant.id as string | null,
      icon: Building2,
    })),
  ]

  const toggle = () => setOpen((value) => !value)

  // Close on Escape; ArrowUp/ArrowDown rove focus across the options.
  useEffect(() => {
    if (!open) return
    const frame = requestAnimationFrame(() => optionRefs.current[0]?.focus())
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setOpen(false)
        triggerRef.current?.focus()
        return
      }
      if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return
      const current = optionRefs.current.findIndex((node) => node === document.activeElement)
      const step = event.key === 'ArrowDown' ? 1 : -1
      const next =
        current === -1
          ? 0
          : (current + step + optionRefs.current.length) % optionRefs.current.length
      optionRefs.current[next]?.focus()
      event.preventDefault()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      cancelAnimationFrame(frame)
      document.removeEventListener('keydown', onKeyDown)
      triggerRef.current?.focus()
    }
  }, [open])

  return (
    <div className="relative">
      <button
        type="button"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={
          activeTenant
            ? `تغییر محدوده؛ اکنون در مستأجر ${activeTenant.name}`
            : onTenantRoute
              ? 'تغییر محدوده؛ اکنون در یک مستأجر'
              : 'تغییر محدوده؛ اکنون در سطح پلتفرم'
        }
        className={cn(
          'inline-flex min-h-10 max-w-40 items-center gap-2 rounded-md border border-border bg-surface px-3 text-sm font-medium transition-colors hover:bg-muted sm:max-w-56',
          (activeTenant || onTenantRoute) && 'border-primary/40 bg-primary/10 text-foreground',
        )}
        ref={(node) => {
          triggerRef.current = node
        }}
        onClick={toggle}
      >
        <span className="inline-flex size-5 shrink-0 items-center justify-center rounded bg-primary/15 text-primary">
          <Building2 aria-hidden="true" className="size-3.5" />
        </span>
        <span className="truncate">
          {activeTenant ? (
            <bdi className="font-semibold">{activeTenant.name}</bdi>
          ) : onTenantRoute ? (
            'مستأجر'
          ) : (
            'پلتفرم'
          )}
        </span>
        <ChevronsUpDown aria-hidden="true" className="size-4 shrink-0 text-muted-foreground" />
      </button>

      {open && (
        <div className="fixed inset-0 z-30" aria-hidden="true" onClick={() => setOpen(false)} />
      )}

      {open && (
        <div
          role="listbox"
          aria-label="انتخاب محدوده"
          className="absolute top-full end-0 z-40 mt-2 w-64 max-w-[calc(100vw-2rem)] overflow-hidden rounded-xl border border-border bg-surface-elevated shadow-raised"
        >
          <p className="border-b border-border px-3 py-2 text-xs font-semibold text-muted-foreground">
            {isBusy && tenants === null ? 'در حال بارگذاری مستأجران…' : 'انتخاب محدوده'}
          </p>
          <ul className="max-h-72 overflow-y-auto p-1">
            {options.map((option, index) => {
              const selected = option.tenantId !== null && option.tenantId === activeTenantId
              const OptionIcon = option.icon
              return (
                <li key={option.key}>
                  <button
                    type="button"
                    role="option"
                    aria-selected={selected}
                    ref={(node) => {
                      optionRefs.current[index] = node
                    }}
                    className={cn(
                      'flex min-h-10 w-full items-center gap-2.5 rounded-md px-2.5 text-sm transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
                      selected ? 'bg-muted font-semibold text-foreground' : 'text-foreground/90',
                    )}
                    onClick={() => {
                      setOpen(false)
                      if (option.tenantId === null) selectPlatform()
                      else selectTenant(option.tenantId)
                    }}
                  >
                    <OptionIcon aria-hidden="true" className="size-4 shrink-0 text-muted-foreground" />
                    <span className="min-w-0 flex-1 truncate text-start">
                      <bdi>{option.label}</bdi>
                    </span>
                    {selected && <Check aria-hidden="true" className="size-4 shrink-0 text-primary" />}
                  </button>
                </li>
              )
            })}
            {isBusy && tenants === null && (
              <li className="flex items-center gap-2 px-2.5 py-2 text-sm text-muted-foreground" role="presentation">
                <Loader2 aria-hidden="true" className="size-4 animate-spin motion-reduce:animate-none" />
                فهرست مستأجران بارگذاری می‌شود…
              </li>
            )}
            {!isBusy && tenants !== null && tenants.length === 0 && (
              <li className="px-2.5 py-2 text-xs text-muted-foreground" role="presentation">
                هنوز مستأجری ایجاد نشده است.
              </li>
            )}
          </ul>
        </div>
      )}
    </div>
  )
}
