import { z } from 'zod'

/**
 * S40 (B044) — the gateway-neutral payment lifecycle wire contract.
 *
 * Field names, casing and nullability mirror the delivered backend records in
 * `src/modules/shop/TenantForge.Modules.Shop/features/payments/PaymentContracts.cs`
 * and the S40 section of `docs/design/shop/http-contracts.md` exactly. B044 is
 * already delivered, so the C# records and the integration tests are the ground
 * truth this file was checked against. This is a MOCK-first task: nothing here
 * is a UI-only shape, and no member is added that B044 does not name.
 *
 * - `InitiatePaymentResponse`  ← POST …/orders/{orderId}/payments/initiate
 *   (anonymous, no body; `Idempotency-Key` UUID header).
 * - `PaymentStatusResponse`    ← GET …/orders/{orderId}/payments/status?token=…
 *   (the sandbox resolve returns the SAME shape).
 * - `ResolveSandboxPaymentRequest` ← the Development-only sandbox resolve body.
 *
 * The one error body any Shop client parses stays `ShopClientError` (the shared
 * RFC 7807 shape) — this file does not introduce a second error type.
 */

export const paymentProviderSchema = z.enum(['Sandbox', 'ZarinPal'])
export type PaymentProvider = z.infer<typeof paymentProviderSchema>

/** The four order statuses the status endpoint can report (B044). */
export const paymentResultStatusSchema = z.enum(['PendingPayment', 'Paid', 'Cancelled', 'Fulfilled'])
export type PaymentResultStatus = z.infer<typeof paymentResultStatusSchema>

export const paymentInitiationSchema = z.object({
  provider: paymentProviderSchema,
  redirectUrl: z.string(),
  resultToken: z.string(),
})
export type PaymentInitiation = z.infer<typeof paymentInitiationSchema>

export const paymentResultSchema = z.object({
  orderNumber: z.string(),
  status: paymentResultStatusSchema,
  providerReference: z.string().nullable(),
})
export type PaymentResult = z.infer<typeof paymentResultSchema>

/**
 * The Development-only sandbox resolve request body. The client port takes
 * `authority` and `approved` as separate parameters (per the required
 * interface shape); this schema records the wire body so the mock parses
 * through the real shape rather than a hand-written copy.
 */
export const resolveSandboxPaymentRequestSchema = z.object({
  authority: z.string().nullable(),
  approved: z.boolean(),
})
export type ResolveSandboxPaymentRequest = z.infer<typeof resolveSandboxPaymentRequestSchema>

// ---------------------------------------------------------------------------
// Redirect safety
//
// B044's `redirectUrl` is the gateway's target: a RELATIVE same-origin path
// for the in-app Sandbox bank (`/shop/{tenantId}/bank?authority=…`) or an
// ABSOLUTE URL for a real provider. It is untrusted input — the browser must
// never navigate to it without vetting the scheme and host. F062 replaces this
// helper with a shared `assertAllowedPaymentRedirect`; until then it lives here
// as the one small function the redirect page calls.
// ---------------------------------------------------------------------------

export type PaymentRedirectCheck =
  | { ok: true; url: string }
  | { ok: false; reason: 'protocol-relative' | 'insecure-scheme' | 'unexpected-host' | 'not-allowed' }

/**
 * Vet a gateway `redirectUrl` before the browser may follow it.
 *
 * - A protocol-relative URL (`//host/path`) is ALWAYS rejected: it looks like a
 *   path but is actually another origin, so it is never same-origin.
 * - An absolute URL must be `https:` (an `http:` gateway is insecure) and — for
 *   a real provider — on an expected host. The Sandbox provider never returns
 *   an absolute URL, so any absolute URL from it is rejected outright.
 * - A relative path is same-origin only for the Sandbox provider; it is its
 *   `/shop/{tenantId}/bank?authority=…` target. Any other relative shape (or a
 *   relative path from a real provider) is rejected.
 *
 * Every miss returns `ok: false` with a distinct reason so the UI can show a
 * precise message; no miss ever leads to a navigation.
 */
export function assertAllowedPaymentRedirect(
  provider: PaymentProvider,
  redirectUrl: string,
  expectedHosts: readonly string[] = [],
): PaymentRedirectCheck {
  if (redirectUrl.startsWith('//')) return { ok: false, reason: 'protocol-relative' }

  let absolute: URL | null = null
  try {
    absolute = new URL(redirectUrl, 'https://placeholder.invalid')
  } catch {
    absolute = null
  }
  const isAbsolute = absolute !== null && !redirectUrl.startsWith('/') && !redirectUrl.startsWith('./')

  if (isAbsolute && absolute) {
    if (absolute.protocol !== 'https:') return { ok: false, reason: 'insecure-scheme' }
    if (provider === 'Sandbox') return { ok: false, reason: 'not-allowed' }
    if (expectedHosts.length > 0 && !expectedHosts.includes(absolute.hostname.toLowerCase())) {
      return { ok: false, reason: 'unexpected-host' }
    }
    return { ok: true, url: redirectUrl }
  }

  if (provider === 'Sandbox') {
    return redirectUrl.startsWith('/') && !redirectUrl.startsWith('//')
      ? { ok: true, url: redirectUrl }
      : { ok: false, reason: 'not-allowed' }
  }

  return { ok: false, reason: 'not-allowed' }
}

/**
 * The gateway hosts a real provider (ZarinPal) may redirect to. Kept here as
 * the allowlist the redirect check consults; F062's shared helper takes this
 * from the configured provider instead.
 */
export const PAYMENT_GATEWAY_HOSTS: readonly string[] = [
  'checkout.zarinpal.com',
  'www.zarinpal.com',
  'ac.zarinpal.com',
]
