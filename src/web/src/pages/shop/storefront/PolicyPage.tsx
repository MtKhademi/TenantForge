import { RefreshCw, Store } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import type { PublicShopProfile } from '@/features/shop/contracts/shopProfileContract'

/**
 * S35 (F047) — one reusable storefront policy page.
 *
 * Renders a single plain-text field of the published shop profile (About,
 * Shipping, Payment, Returns or Privacy) chosen by the `field` prop; `App.tsx`
 * mounts it five times under `/shop/:tenantId/{about,shipping,payment,
 * returns,privacy}`.
 *
 * The text is rendered as **plain text only**: React escapes it and
 * `whitespace-pre-line` turns the stored newlines into visual line breaks.
 * Nothing is ever interpreted as HTML.
 *
 * States:
 * - loading: a fixed-height skeleton (no layout shift when content lands);
 * - published + content: the policy text;
 * - published + empty text: a neutral "not set yet" note (an empty result,
 *   not an error);
 * - unpublished or missing profile (`getPublic` → null, the anonymous route's
 *   404): the SAME neutral fallback the layout header/footer use — the
 *   designed "not yet open" state, never an error page;
 * - unavailable: a retryable error (the request could not be made at all).
 *
 * Data flows only through `useShopClients().profile` — never a fixture and
 * never `fetch`.
 */

export type PolicyField = 'about' | 'shipping' | 'payment' | 'returns' | 'privacy'

const POLICY_META: Record<PolicyField, { title: string; description: string; textKey: 'aboutText' | 'shippingPolicy' | 'paymentPolicy' | 'returnPolicy' | 'privacyPolicy' }> = {
  about: {
    title: 'درباره ما',
    description: 'معرفی فروشگاه برای مشتریان',
    textKey: 'aboutText',
  },
  shipping: {
    title: 'شرایط ارسال',
    description: 'نحوه و زمان ارسال سفارش‌ها',
    textKey: 'shippingPolicy',
  },
  payment: {
    title: 'شرایط پرداخت',
    description: 'روش‌های پرداخت قابل انتخاب',
    textKey: 'paymentPolicy',
  },
  returns: {
    title: 'شرایط مرجوعی',
    description: 'قواعد بازگشت کالا',
    textKey: 'returnPolicy',
  },
  privacy: {
    title: 'حریم خصوصی',
    description: 'نحوه پردازش اطلاعات مشتریان',
    textKey: 'privacyPolicy',
  },
}

type State =
  | { status: 'idle' | 'loading' }
  | { status: 'content'; profile: PublicShopProfile }
  | { status: 'notOpen' }
  | { status: 'unavailable' }

export function PolicyPage({ field }: { field: PolicyField }) {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const { profile: profileClient } = useShopClients()
  const [state, setState] = useState<State>({ status: 'idle' })
  const [reloadKey, setReloadKey] = useState(0)

  // The single data request. A new run aborts the previous one, so a
  // superseded response can never overwrite the newer state, and an aborted
  // call never surfaces as an error.
  useEffect(() => {
    const controller = new AbortController()
    setState({ status: 'loading' })

    profileClient
      .getPublic(tenantId, controller.signal)
      .then((result) => {
        if (controller.signal.aborted) return
        setState(result ? { status: 'content', profile: result } : { status: 'notOpen' })
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        if (error instanceof DOMException && error.name === 'AbortError') return
        // A transport failure (or anything unexpected) is the retryable
        // "unavailable" state — the profile is simply not reachable right now.
        setState({ status: 'unavailable' })
      })

    return () => controller.abort()
  }, [tenantId, reloadKey, profileClient])

  const meta = POLICY_META[field]
  const text = state.status === 'content' ? state.profile[meta.textKey] : ''

  return (
    <section aria-label={meta.title} className="mx-auto max-w-3xl space-y-6">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold">{meta.title}</h1>
        <p className="text-sm text-muted-foreground">{meta.description}</p>
      </header>

      {state.status === 'loading' || state.status === 'idle' ? (
        <div className="min-h-[240px] space-y-3" aria-busy="true" aria-live="polite">
          <div className="h-5 w-2/3 animate-pulse rounded-md bg-muted" />
          <div className="h-4 w-full animate-pulse rounded-md bg-muted" />
          <div className="h-4 w-full animate-pulse rounded-md bg-muted" />
          <div className="h-4 w-3/4 animate-pulse rounded-md bg-muted" />
        </div>
      ) : state.status === 'unavailable' ? (
        <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 p-6 text-center">
          <p className="text-sm font-semibold">اتصال به فروشگاه برقرار نشد</p>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">اتصال را دوباره امتحان کنید.</p>
          <button
            type="button"
            onClick={() => setReloadKey((key) => key + 1)}
            className="mt-3 inline-flex min-h-10 items-center justify-center rounded-md border border-border bg-surface px-4 py-2 text-sm font-semibold text-foreground transition-colors duration-150 ease-admin hover:bg-muted"
          >
            <RefreshCw aria-hidden="true" className="me-2 size-4" />
            تلاش مجدد
          </button>
        </div>
      ) : state.status === 'notOpen' ? (
        <NotOpenPanel />
      ) : text.trim().length === 0 ? (
        <div className="rounded-lg border border-dashed border-border bg-muted/30 p-6 text-center">
          <p className="text-sm text-muted-foreground">هنوز محتوایی برای این صفحه ثبت نشده است.</p>
        </div>
      ) : (
        <div className="rounded-lg border border-border bg-surface p-5 md:p-6">
          {/* Plain text only: React escapes the value; pre-line keeps the
              stored newlines as visual line breaks and never parses HTML. */}
          <p className="whitespace-pre-line text-sm leading-7 text-foreground">{text}</p>
        </div>
      )}
    </section>
  )
}

/**
 * The honest "this store is not yet open" state — the SAME neutral fallback
 * the layout header and footer render for an unpublished/missing profile, so
 * the store never shows real content on some surfaces and a fallback on
 * others.
 */
export function NotOpenPanel() {
  return (
    <div className="rounded-lg border border-border bg-surface p-8 text-center">
      <span className="mx-auto mb-3 inline-flex size-12 items-center justify-center rounded-full bg-muted">
        <Store aria-hidden="true" className="size-6 text-muted-foreground" />
      </span>
      <p className="text-base font-semibold">این فروشگاه هنوز آماده‌سازی نشده است</p>
      <p className="mx-auto mt-1 max-w-md text-sm leading-6 text-muted-foreground">
        صاحب فروشگاه هنوز این بخش را منتشر نکرده است. لطفاً کمی بعد دوباره سر بزنید.
      </p>
    </div>
  )
}
