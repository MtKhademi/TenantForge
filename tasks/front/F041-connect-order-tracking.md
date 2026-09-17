---
id: F041
slice: S30
title: Connect order tracking page to the real API
agent: ui-engineer
source: tasks/slices/030-guest-order-tracking.md
---

# Objective

Replace F040's mocked order-tracking lookup with a real call to B033's
order lookup API.

This Spec gives you the exact adapter function (matching B033's
`OrderLookupContracts.cs` field-for-field) and the exact call-site
change in `OrderTrackingPage.tsx`. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/030-guest-order-tracking.md`. Read B033's
delivered endpoint shape (from its integration tests or, if the Spec
file still exists at task start,
`tasks/backend/B033-guest-order-lookup-api.md`) before writing the real
adapter.

# Scope — every file, in order

## 1. `src/web/src/features/shop/orderLookupAdapter.ts`

```typescript
import { ApiUnavailableError } from '@/features/auth/authTypes'
import type { OrderLookupResult } from './mockOrderLookup'

const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

/**
 * S30 guest order lookup — real, anonymous API data source (F041),
 * replacing F040's mock. No Authorization header is ever sent. A 404
 * (wrong pair, or a wholly nonexistent tracking code) resolves to
 * `null` — the exact same generic outcome for both cases, since B033's
 * real API never distinguishes them either (see this Spec's Context).
 */
export async function lookupOrder(
  tenantId: string,
  trackingCode: string,
  customerPhone: string,
): Promise<OrderLookupResult | null> {
  const abort = createRequestAbortSignal()
  let response: Response
  try {
    response = await fetch(`/api/shop/${tenantId}/orders/lookup`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ trackingCode, customerPhone }),
      signal: abort.signal,
    })
  } catch {
    throw new ApiUnavailableError()
  } finally {
    abort.clear()
  }

  if (response.status === 404) return null
  if (!response.ok) throw new ApiUnavailableError()

  try {
    return (await response.json()) as OrderLookupResult
  } catch {
    throw new ApiUnavailableError()
  }
}
```

Note `OrderLookupResult`'s type (imported from F040's now-mock-only
`mockOrderLookup.ts`) already matches B033's real response
field-for-field — move that type declaration into a new
`orderLookupTypes.ts` file (or leave it in `mockOrderLookup.ts` and
change this import once that file is trimmed in step 3) before deleting
the mock function around it.

## 2. Update `OrderTrackingPage.tsx`

Replace the import
`import { mockLookupOrder, type OrderLookupResult } from
'@/features/shop/mockOrderLookup'` with:

```tsx
import { lookupOrder } from '@/features/shop/orderLookupAdapter'
import type { OrderLookupResult } from '@/features/shop/orderLookupTypes'
```

Replace the `onSubmit` body's `mockLookupOrder(values.trackingCode,
values.customerPhone)` call with
`lookupOrder(tenantId, values.trackingCode, values.customerPhone)` —
add `const { tenantId = '' } = useParams<{ tenantId: string }>()` to the
component if not already present. The rendered result/not-found JSX
does not change at all: F040 already built the one generic not-found
message this task's real 404 case reuses unchanged.

## 3. Delete the mock

Move `OrderLookupItem`/`OrderLookupResult` from `mockOrderLookup.ts`
into `src/web/src/features/shop/orderLookupTypes.ts`, then delete
`mockOrderLookup.ts` (its `mockLookupOrder` function and the two mock
constants) entirely.

# Non-goals

- No order-modification action.

# If you get stuck

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/orders/lookup \
  -H "Content-Type: application/json" \
  -d '{"trackingCode":"<trackingCode>","customerPhone":"09121234567"}'
```

Run this once with a correct pair and once with a wrong phone number for
the same tracking code; confirm both non-matching cases return the exact
same `404` body (same `title`/`detail`) before wiring the page — B033's
own Spec requires this identical-response guarantee, and this task's UI
has nothing to branch on if the two ever differ.

# Acceptance

- A real, correct tracking-code + phone pair shows the real order's
  status/items/totals.
- A real tracking code with a wrong phone number, and a wholly made-up
  tracking code, both show the identical generic not-found message.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Look up a real order placed earlier (F039) with its correct tracking
  code and phone, then with a wrong phone number for the same code, and
  confirm the two outcomes described above.
- No new browser console error.

# Lifecycle

Add row `F041` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F040, B033`, and Spec link
`tasks/front/F041-connect-order-tracking.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/030-guest-order-tracking.md` is the permanent
record and is never deleted.
