import { z } from 'zod'

/**
 * S35 (B039) — tenant storefront identity and policy content, wire shape.
 *
 * Mirrors the C# records of the paired backend Spec `B039` and the
 * persistent contract section `## S35 / B039 — profile and policies` in
 * `docs/design/shop/http-contracts.md`, field for field. The admin GET
 * returns `{ profile: ShopProfile | null }` (200 with null before the first
 * save — the "null-first" setup state, never a 404). The anonymous public
 * GET returns `PublicShopProfile` (no `id`, no `tenantId`, no `version`,
 * no `updatedAtUtc`) or 404 when the profile is missing or unpublished.
 *
 * `SaveShopProfileRequest` is the PUT body: the admin fields plus
 * `expectedVersion` — `null` for the first save (create), the last-seen
 * `version` for updates. A stale `expectedVersion` is a `409`
 * (`type: 'stale_version'`).
 */
export const shopProfileSchema = z.object({
  id: z.string(),
  tenantId: z.string(),
  name: z.string(),
  tagline: z.string(),
  supportPhone: z.string(),
  instagramUrl: z.string().nullable(),
  aboutText: z.string(),
  shippingPolicy: z.string(),
  paymentPolicy: z.string(),
  returnPolicy: z.string(),
  privacyPolicy: z.string(),
  isPublished: z.boolean(),
  version: z.number().int(),
  updatedAtUtc: z.string(),
})
export type ShopProfile = z.infer<typeof shopProfileSchema>

/**
 * The customer-facing subset of the profile: the published identity and the
 * five plain-text policy/about fields. `isPublished` is included because the
 * C# `PublicShopProfileResponse` carries it; `id`, `tenantId`, `version` and
 * `updatedAtUtc` never leave the server on the public route.
 */
export const publicShopProfileSchema = z.object({
  name: z.string(),
  tagline: z.string(),
  supportPhone: z.string(),
  instagramUrl: z.string().nullable(),
  aboutText: z.string(),
  shippingPolicy: z.string(),
  paymentPolicy: z.string(),
  returnPolicy: z.string(),
  privacyPolicy: z.string(),
  isPublished: z.boolean(),
})
export type PublicShopProfile = z.infer<typeof publicShopProfileSchema>

/**
 * PUT body: `ShopProfile` minus the server-owned `id`, `tenantId`, `version`
 * and `updatedAtUtc`, plus `expectedVersion` (null = create).
 */
export type SaveShopProfileRequest = Omit<ShopProfile, 'id' | 'tenantId' | 'version' | 'updatedAtUtc'> & {
  expectedVersion: number | null
}
