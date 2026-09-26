/**
 * S40 (B044) — a small, per-tenant carrier for the in-flight payment's opaque
 * `resultToken` and `orderId` (F052, mock phase).
 *
 * B044's Sandbox `redirectUrl` is exactly `/shop/{tenantId}/bank?authority=…`
 * (the authority is the gateway identifier) — the `resultToken` is NOT carried
 * in that URL. The bank page needs the token so it can hand it to the result
 * page (which resolves purely from it, so a refresh still works). This carrier
 * stores the pair for the current tenant in sessionStorage, mirroring
 * `orderDraftState`'s per-tenant-record convention. The bank clears it once it
 * has handed the token to the result page, so it is never read as a source of
 * truth by the result page itself.
 */

const PAYMENT_FLOW_KEY = 'tenantforge:shop:paymentFlow'

export interface PaymentFlow {
  orderId: string
  resultToken: string
}

function readRecord(): Record<string, PaymentFlow> {
  try {
    const raw = window.sessionStorage.getItem(PAYMENT_FLOW_KEY)
    if (!raw) return {}
    const parsed: unknown = JSON.parse(raw)
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return {}
    return parsed as Record<string, PaymentFlow>
  } catch {
    return {}
  }
}

function writeRecord(record: Record<string, unknown>): void {
  try {
    window.sessionStorage.setItem(PAYMENT_FLOW_KEY, JSON.stringify(record))
  } catch {
    // Best-effort only.
  }
}

export function savePaymentFlow(tenantId: string, flow: PaymentFlow): void {
  const record = readRecord()
  record[tenantId] = flow
  writeRecord(record)
}

/** This tenant's in-flight payment flow, or null when none is stored. */
export function loadPaymentFlow(tenantId: string): PaymentFlow | null {
  return readRecord()[tenantId] ?? null
}

/** Clear ONLY this tenant's in-flight payment flow. */
export function clearPaymentFlow(tenantId: string): void {
  const record = readRecord()
  if (!(tenantId in record)) return
  delete record[tenantId]
  writeRecord(record)
}
