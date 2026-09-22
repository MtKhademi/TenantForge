import { ApiUnavailableError } from '@/features/auth/authTypes'
import {
  adminCategorySchema,
  type AdminCategory,
  type PublicCategory,
  type SaveCategoryRequest,
} from '../contracts/categoryHierarchyContract'
import { ShopClientError } from '../contracts/shopContract'
import { delay } from './shopFetch'
import type { ShopCategoryClient } from './ShopCategoryClient'

/**
 * S34 category hierarchy — deterministic mock implementation of
 * `ShopCategoryClient` (F046). F056 swaps in `httpShopCategoryClient` behind
 * the same interface. Scenario names live only in this file and the dev-only
 * switcher; they never reach product-facing markup.
 *
 * Behavior mirrors B038: categories are never more than two levels deep
 * (root + one direct child level). A supplied parent must exist in this
 * tenant, be active, be itself a root, and not be the category itself — all
 * four violations return the SAME field validation error on
 * `parentCategoryId` (`400`). Reparenting a category that already has
 * children is a conflict (`409`), also on `parentCategoryId` (see
 * `docs/design/shop/http-contracts.md` §S34). The public tree applies
 * "effective activity": a child is public only while both it and its root
 * are active. Latency is simulated through the shared abort-aware `delay`
 * helper, so a superseded request rejects with `AbortError` instead of
 * resolving into stale UI.
 *
 * Mutations persist in memory for the page lifetime (the same lifetime the
 * F044/F045 mocks use); a reload re-seeds the store deterministically.
 */

const READ_LATENCY_MS = 600

export type ShopCategoryScenarioKey =
  | 'populated'
  | 'empty'
  | 'unavailable'
  | 'forbidden'
  | 'missingCategory'
  | 'invalidThirdLevel'
  | 'inactiveParent'
  | 'parentHasChildren'

export interface ShopCategoryScenario {
  key: ShopCategoryScenarioKey
  label: string
}

export const SHOP_CATEGORY_SCENARIOS: readonly ShopCategoryScenario[] = [
  { key: 'populated', label: 'فهرست پرشده' },
  { key: 'empty', label: 'فهرست خالی' },
  { key: 'unavailable', label: 'اتصال در دسترس نیست' },
  { key: 'forbidden', label: 'بدون مجوز (403)' },
  { key: 'missingCategory', label: 'دسته‌ی ناموجود (404)' },
  { key: 'invalidThirdLevel', label: 'سطح ثالث (400)' },
  { key: 'inactiveParent', label: 'والد غیرفعال (400)' },
  { key: 'parentHasChildren', label: 'والد با زیر‌دسته (409)' },
] as const

const SCENARIO_STORAGE_KEY = 'tfCategoryScenario'

function readStoredScenarioKey(): ShopCategoryScenarioKey {
  try {
    const stored = window.sessionStorage.getItem(SCENARIO_STORAGE_KEY)
    if (SHOP_CATEGORY_SCENARIOS.some((entry) => entry.key === stored)) {
      return stored as ShopCategoryScenarioKey
    }
  } catch {
    // Storage can be unavailable; the default scenario wins.
  }
  return SHOP_CATEGORY_SCENARIOS[0].key
}

let activeScenarioKey: ShopCategoryScenarioKey = readStoredScenarioKey()

export function getShopCategoryScenarioKey(): ShopCategoryScenarioKey {
  return activeScenarioKey
}

export function setShopCategoryScenario(key: ShopCategoryScenarioKey): void {
  if (!SHOP_CATEGORY_SCENARIOS.some((entry) => entry.key === key)) {
    throw new Error(`Unknown shop category scenario: ${key}`)
  }
  activeScenarioKey = key
  try {
    window.sessionStorage.setItem(SCENARIO_STORAGE_KEY, key)
  } catch {
    // Non-fatal: the scenario still applies for this page lifetime.
  }
}

// Fixture data -----------------------------------------------------------------

// Canonical 13-character TSID-style IDs, stable and time-sortable.
const TENANT_ID = 't0000000000001'

function tsid(sequence: number): string {
  return `c${String(sequence).padStart(12, '0')}`
}

/**
 * Seed: five roots (four active, one inactive) and seven direct children.
 * `shoes`/`bags`/`accessories` are the slugs the F045 discovery mock already
 * resolves, so catalog category-filter links keep working across both mocks.
 * `watches` is an active root with no children (existing root-only shape);
 * `legacy` is an inactive root with one active child — the pair that
 * demonstrates effective public activity (both rows stored, neither public).
 */
const SEED_ROWS: readonly AdminCategory[] = [
  // Roots
  { id: tsid(1), tenantId: TENANT_ID, name: 'کفش', slug: 'shoes', displayOrder: 100, isActive: true, parentCategoryId: null },
  { id: tsid(2), tenantId: TENANT_ID, name: 'کیف', slug: 'bags', displayOrder: 200, isActive: true, parentCategoryId: null },
  { id: tsid(3), tenantId: TENANT_ID, name: 'لوازم جانبی', slug: 'accessories', displayOrder: 300, isActive: true, parentCategoryId: null },
  { id: tsid(4), tenantId: TENANT_ID, name: 'ساعت‌ها', slug: 'watches', displayOrder: 400, isActive: true, parentCategoryId: null },
  { id: tsid(5), tenantId: TENANT_ID, name: 'دسته‌بندی قدیمی', slug: 'legacy', displayOrder: 500, isActive: false, parentCategoryId: null },
  // Direct children
  { id: tsid(6), tenantId: TENANT_ID, name: 'کفش ورزشی', slug: 'sport-shoes', displayOrder: 110, isActive: true, parentCategoryId: tsid(1) },
  { id: tsid(7), tenantId: TENANT_ID, name: 'کفش رسمی', slug: 'formal-shoes', displayOrder: 120, isActive: true, parentCategoryId: tsid(1) },
  { id: tsid(8), tenantId: TENANT_ID, name: 'کیف دستی', slug: 'handbags', displayOrder: 210, isActive: true, parentCategoryId: tsid(2) },
  { id: tsid(9), tenantId: TENANT_ID, name: 'کیف لپ‌تاپ', slug: 'laptop-bags', displayOrder: 220, isActive: true, parentCategoryId: tsid(2) },
  { id: tsid(10), tenantId: TENANT_ID, name: 'لوازم موبایل', slug: 'phone-accessories', displayOrder: 310, isActive: true, parentCategoryId: tsid(3) },
  { id: tsid(11), tenantId: TENANT_ID, name: 'لوازم صوتی', slug: 'audio', displayOrder: 320, isActive: false, parentCategoryId: tsid(3) },
  { id: tsid(12), tenantId: TENANT_ID, name: 'محصولات قدیمی', slug: 'legacy-products', displayOrder: 510, isActive: true, parentCategoryId: tsid(5) },
] as const

let rows: AdminCategory[] = SEED_ROWS.map((row) => adminCategorySchema.parse(row))
let nextSequence = SEED_ROWS.length + 1

// Error helpers — every error uses the shared RFC7807 `ShopClientError` shape.

/**
 * B038's single parent-validation failure: self-parent, third level, inactive
 * parent and foreign-tenant parent all return this SAME `parentCategoryId`
 * field error, so the UI never leaks which check failed.
 */
function parentValidationError(): ShopClientError {
  return new ShopClientError({
    status: 400,
    title: 'مقادیر ارسالی معتبر نیست.',
    detail: 'ریشه‌ای نامعتبر انتخاب شده است.',
    errors: { parentCategoryId: ['یک ریشه‌ای فعال را انتخاب کنید.'] },
  })
}

function reparentConflictError(): ShopClientError {
  return new ShopClientError({
    status: 409,
    title: 'انتقال دسته‌بندی ممکن نیست.',
    detail: 'دسته‌بندی‌ای که زیر‌دسته دارد نمی‌تواند زیر‌دسته‌ی دیگری شود.',
    errors: { parentCategoryId: ['این دسته‌بندی دارای زیر‌دسته است و قابل انتقال نیست.'] },
  })
}

function missingCategoryError(): ShopClientError {
  return new ShopClientError({
    status: 404,
    title: 'این دسته‌بندی پیدا نشد یا دیگر در دسترس نیست.',
    detail: 'این دسته‌بندی پیدا نشد یا دیگر در دسترس نیست.',
  })
}

function forbiddenError(): ShopClientError {
  return new ShopClientError({
    status: 403,
    title: 'دسترسی مدیریت فروشگاه مجاز نیست.',
    detail: 'حساب فعلی مجوز مدیریت فروشگاه این مستأجر را ندارد.',
  })
}

// B038 rules --------------------------------------------------------------------

function assertNotAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
}

/** B038 `ValidateParentAsync`: parent must exist, be active, be a root, and not be itself. */
function validateParent(
  currentRows: readonly AdminCategory[],
  categoryId: string,
  parentId: string | null,
): ShopClientError | null {
  if (parentId === null) return null
  const parent = currentRows.find((row) => row.id === parentId)
  if (parent === undefined || !parent.isActive || parent.parentCategoryId !== null || parent.id === categoryId) {
    return parentValidationError()
  }
  return null
}

/** B038 reparent guard: a parent that currently has children can never become a child. */
function reparentGuard(currentRows: readonly AdminCategory[], categoryId: string): ShopClientError | null {
  if (currentRows.some((row) => row.parentCategoryId === categoryId)) {
    return reparentConflictError()
  }
  return null
}

function publicTree(currentRows: readonly AdminCategory[]): PublicCategory[] {
  const roots = currentRows
    .filter((row) => row.parentCategoryId === null && row.isActive)
    .sort((a, b) => a.displayOrder - b.displayOrder)
    .map((root) => ({
      id: root.id,
      name: root.name,
      slug: root.slug,
      displayOrder: root.displayOrder,
      children: currentRows
        .filter((row) => row.parentCategoryId === root.id && row.isActive)
        .sort((a, b) => a.displayOrder - b.displayOrder)
        .map((child) => ({
          id: child.id,
          name: child.name,
          slug: child.slug,
          displayOrder: child.displayOrder,
          children: [] as PublicCategory[],
        })),
    }))
  // The items are built from schema-parsed `AdminCategory` rows, so they are
  // structurally `PublicCategory` values; the `publicCategorySchema` is the
  // single-item (recursive) schema the HTTP client will parse through.
  return roots
}

// Client --------------------------------------------------------------------------

export const mockShopCategoryClient: ShopCategoryClient = {
  async listAdmin(_tenantId, signal) {
    if (activeScenarioKey === 'unavailable') {
      await delay(READ_LATENCY_MS, signal)
      assertNotAborted(signal)
      throw new ApiUnavailableError()
    }
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)
    if (activeScenarioKey === 'forbidden') throw forbiddenError()
    if (activeScenarioKey === 'empty') return []
    return rows.map((row) => adminCategorySchema.parse(row))
  },

  async create(_tenantId, body, signal) {
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)
    if (activeScenarioKey === 'forbidden') throw forbiddenError()
    // The named scenario stands in for attempting to create a grandchild:
    // the page's parent selector can never offer a child, so the attempt is
    // simulated here and rejected with the exact B038 parent-validation error.
    if (activeScenarioKey === 'invalidThirdLevel') throw parentValidationError()
    const problem = validateParent(rows, '', body.parentCategoryId)
    if (problem !== null) throw problem
    const created = adminCategorySchema.parse({
      id: tsid(nextSequence++),
      tenantId: TENANT_ID,
      name: body.name,
      slug: body.slug,
      displayOrder: body.displayOrder,
      isActive: true, // B038: a new category's active state is decided by the backend; the mock defaults to active
      parentCategoryId: body.parentCategoryId,
    })
    rows = [...rows, created]
    return created
  },

  async update(_tenantId, categoryId, body: SaveCategoryRequest, signal) {
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)
    if (activeScenarioKey === 'forbidden') throw forbiddenError()
    const existing = rows.find((row) => row.id === categoryId)
    if (existing === undefined || activeScenarioKey === 'missingCategory') throw missingCategoryError()
    // Named scenarios stand in for the two reparent attempts B038 guards:
    // moving under an inactive parent (400) and reparenting a parent that
    // already has children (409).
    if (activeScenarioKey === 'inactiveParent' && body.parentCategoryId !== null) throw parentValidationError()
    if (activeScenarioKey === 'parentHasChildren' && body.parentCategoryId !== null) throw reparentConflictError()
    const problem = validateParent(rows, categoryId, body.parentCategoryId)
    if (problem !== null) throw problem
    if (body.parentCategoryId !== null && existing.parentCategoryId !== body.parentCategoryId) {
      const guard = reparentGuard(rows, categoryId)
      if (guard !== null) throw guard
    }
    const updated = adminCategorySchema.parse({
      ...existing,
      name: body.name,
      slug: body.slug,
      displayOrder: body.displayOrder,
      isActive: body.isActive,
      parentCategoryId: body.parentCategoryId,
    })
    rows = rows.map((row) => (row.id === categoryId ? updated : row))
    return updated
  },

  async listPublic(_tenantId, signal) {
    if (activeScenarioKey === 'unavailable') {
      await delay(READ_LATENCY_MS, signal)
      assertNotAborted(signal)
      throw new ApiUnavailableError()
    }
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)
    if (activeScenarioKey === 'empty') return []
    return publicTree(rows)
  },
}
