---
id: F037
slice: S28
title: Connect checkout page to the real API
agent: ui-engineer
source: tasks/slices/028-shop-checkout.md
---

# Objective

Replace F036's mocked checkout summary computation with real calls to
B030's checkout API.

This Spec gives you the exact adapter function and the exact debounced
recompute wiring. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/028-shop-checkout.md`. Read B030's delivered
endpoint shape (from its integration tests or, if the Spec file still
exists at task start, `tasks/backend/B030-checkout-summary-api.md`)
before writing the real adapter. Read F033's delivered
`cartStorage.ts`'s `getCartId()` — this task's summary call needs the
stored cart id.

# Scope — every file, in order

## 1. `src/web/src/features/shop/checkoutAdapter.ts`

Field names copied from B030's `CheckoutContracts.cs`, camelCased:

```typescript
import { ApiUnavailableError } from '@/features/auth/authTypes'
import { getCartId } from './cartStorage'

export type CheckoutSummaryRequest = {
  cartId: string
  shippingProvince: string
  shippingCity: string
  shippingAddressLine: string
  shippingPostalCode: string
  couponCode: string | null
}

export type CheckoutSummaryResponse = {
  subTotal: number
  discountAmount: number
  shippingCost: number
  grandTotal: number
}

export class CheckoutValidationError extends Error {
  fieldErrors: Record<string, string>

  constructor(fieldErrors: Record<string, string>) {
    super('اطلاعات وارد شده معتبر نیست.')
    this.fieldErrors = fieldErrors
    this.name = 'CheckoutValidationError'
  }
}

export class EmptyCartError extends Error {
  constructor(message = 'سبد خرید یافت نشد یا خالی است.') {
    super(message)
    this.name = 'EmptyCartError'
  }
}

const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

function mapServerValidation(payload: unknown): Record<string, string> {
  const fallback = { _: 'مقدار واردشده معتبر نیست.' }
  if (typeof payload !== 'object' || payload === null) return fallback
  const errors = (payload as Record<string, unknown>).errors
  if (typeof errors !== 'object' || errors === null) return fallback
  const mapped: Record<string, string> = {}
  for (const [field, value] of Object.entries(errors as Record<string, unknown>)) {
    if (Array.isArray(value) && typeof value[0] === 'string') mapped[field] = value[0]
  }
  return Object.keys(mapped).length > 0 ? mapped : fallback
}

export async function fetchCheckoutSummary(
  tenantId: string,
  fields: Omit<CheckoutSummaryRequest, 'cartId'>,
): Promise<CheckoutSummaryResponse> {
  const cartId = getCartId()
  if (!cartId) throw new EmptyCartError()

  const abort = createRequestAbortSignal()
  let response: Response
  try {
    response = await fetch(`/api/shop/${tenantId}/checkout/summary`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ cartId, ...fields }),
      signal: abort.signal,
    })
  } catch {
    throw new ApiUnavailableError()
  } finally {
    abort.clear()
  }

  if (response.status === 404) throw new EmptyCartError()
  if (response.status === 400) {
    let payload: unknown
    try {
      payload = await response.json()
    } catch {
      throw new ApiUnavailableError()
    }
    throw new CheckoutValidationError(mapServerValidation(payload))
  }
  if (!response.ok) throw new ApiUnavailableError()

  try {
    return (await response.json()) as CheckoutSummaryResponse
  } catch {
    throw new ApiUnavailableError()
  }
}
```

## 2. Update `CheckoutPage.tsx`

Replace the import
`import { computeMockCheckoutSummary, type CheckoutSummary } from
'@/features/shop/mockCheckoutSummary'` with:

```tsx
import { fetchCheckoutSummary, type CheckoutSummaryResponse } from '@/features/shop/checkoutAdapter'
```

Replace every `CheckoutSummary` type reference with
`CheckoutSummaryResponse`. Replace the `recompute` callback's body: it
currently calls `computeMockCheckoutSummary(province, couponCode)` with
just those two fields; the real call needs every address field plus a
300ms debounce so rapid typing does not fire one request per keystroke:

```tsx
const debounceRef = useRef<number | null>(null)

const recompute = useCallback(() => {
  if (debounceRef.current) window.clearTimeout(debounceRef.current)
  debounceRef.current = window.setTimeout(async () => {
    const values = getValues()
    if (!values.shippingProvince) {
      setSummary(null)
      setSummaryError(null)
      return
    }
    try {
      const result = await fetchCheckoutSummary(tenantId, {
        shippingProvince: values.shippingProvince,
        shippingCity: values.shippingCity,
        shippingAddressLine: values.shippingAddressLine,
        shippingPostalCode: values.shippingPostalCode,
        couponCode: values.couponCode || null,
      })
      setSummary(result)
      setSummaryError(null)
    } catch (error) {
      setSummary(null)
      setSummaryError(error instanceof Error ? error.message : 'خطایی رخ داد.')
    }
  }, 300)
}, [tenantId, getValues])
```

Add `getValues` to the destructured `useForm()` result (alongside
`register`, `watch`, `formState`), and keep the existing `useEffect(() =>
{ void recompute() }, [recompute, province, couponCode])` trigger — but
since `recompute` now reads every field via `getValues()`, also widen
that effect's dependency array to every field `watch()` returns that
should trigger a recompute (province, city, addressLine, postalCode,
couponCode), matching this task's own Acceptance ("recomputed on every
relevant field change").

## 3. Delete the mock

Delete `src/web/src/features/shop/mockCheckoutSummary.ts`.

# Non-goals

- No order creation or payment (F038/F039).

# If you get stuck

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/checkout/summary \
  -H "Content-Type: application/json" \
  -d '{"cartId":"<cartId>","shippingProvince":"تهران","shippingCity":"تهران","shippingAddressLine":"خیابان ولیعصر","shippingPostalCode":"1234567890","couponCode":"WELCOME10"}'
```

Compare the response shape byte-for-byte against
`CheckoutSummaryResponse` above before wiring the page.

# Acceptance

- The summary reflects the real API's computed totals as the shopper
  edits the address/coupon fields.
- A real unshippable province and a real invalid/expired coupon both show
  the API's distinct error messages.
- No mock computation path remains reachable.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- With a real cart (from F033) and real shipping rates/coupons (from
  F035), fill in a shippable province with a valid coupon and confirm
  the correct totals; try an unshippable province and an invalid coupon
  and confirm the clear errors.
- No new browser console error.

# Lifecycle

Add row `F037` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F036, B030`, and Spec link
`tasks/front/F037-connect-checkout-page.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
