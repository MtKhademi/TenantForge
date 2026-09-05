import { ApiUnavailableError } from '@/features/auth/authTypes'
import {
  TenantConflictError,
  type CreateTenantRequest,
  type TenantListResponse,
  type TenantStatus,
  type TenantSummary,
} from './tenantTypes'

/**
 * S07 tenant membership — mock data source (F010).
 *
 * F010 ships the S07 demo with no backend yet: this adapter stores tenants
 * in `sessionStorage` (so a refresh keeps them, and closing the tab resets
 * the demo) and simulates request latency so the real loading states are
 * exercised. The interface mirrors what the F011 HTTP adapter will call —
 * B007's `GET/POST /api/platform/tenants` — so swapping data sources later
 * touches no page code.
 */
export type TenantAdapter = {
  listTenants(accessToken: string): Promise<TenantListResponse>
  createTenant(accessToken: string, request: CreateTenantRequest): Promise<TenantSummary>
}

const STORAGE_KEY = 'tenantforge.tenants.v1'
/** Small artificial latency so skeleton/submitting states are visible. */
const MOCK_LATENCY_MS = 450
const SLUG_PATTERN = /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/

/**
 * `POST /api/platform/tenants` client-side validation failure (HTTP 400 in
 * F011). Field keys are the request field names, exactly like B006's user
 * validation, so the page maps server field errors identically in both.
 */
export class TenantValidationError extends Error {
  fieldErrors: Partial<Record<keyof CreateTenantRequest, string>>

  constructor(fieldErrors: Partial<Record<keyof CreateTenantRequest, string>>) {
    super('درخواست ایجاد مستأجر معتبر نیست.')
    this.fieldErrors = fieldErrors
    this.name = 'TenantValidationError'
  }
}

export class TenantForbiddenError extends Error {
  constructor(message = 'شما اجازه مدیریت مستأجران پلتفرم را ندارید.') {
    super(message)
    this.name = 'TenantForbiddenError'
  }
}

function delay(ms: number) {
  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, ms)
  })
}

function isTenantStatus(value: unknown): value is TenantStatus {
  return value === 'Active' || value === 'Suspended'
}

/**
 * Normalize a tenant slug the way B007 will: trim, casefold, join words with
 * a single dash, drop anything that is not a letter/digit/dash, and never
 * allow a leading or trailing dash.
 */
export function normalizeTenantSlug(raw: string): string {
  return raw
    .trim()
    .toLowerCase()
    .replace(/[\s_]+/g, '-')
    .replace(/[^a-z0-9-]/g, '')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '')
}

function parseTenant(payload: unknown): TenantSummary {
  if (typeof payload !== 'object' || payload === null) {
    throw new ApiUnavailableError()
  }
  const tenant = payload as Record<string, unknown>
  if (
    typeof tenant.id !== 'string' ||
    tenant.id.length === 0 ||
    typeof tenant.name !== 'string' ||
    tenant.name.length === 0 ||
    typeof tenant.slug !== 'string' ||
    tenant.slug.length === 0 ||
    !isTenantStatus(tenant.status) ||
    typeof tenant.memberCount !== 'number' ||
    !Number.isInteger(tenant.memberCount) ||
    tenant.memberCount < 1 ||
    typeof tenant.createdAtUtc !== 'string' ||
    Number.isNaN(Date.parse(tenant.createdAtUtc))
  ) {
    throw new ApiUnavailableError()
  }
  return {
    id: tenant.id,
    name: tenant.name,
    slug: tenant.slug,
    status: tenant.status,
    memberCount: tenant.memberCount,
    createdAtUtc: tenant.createdAtUtc,
  }
}

function readStore(): TenantSummary[] {
  let raw: string | null
  try {
    raw = window.sessionStorage.getItem(STORAGE_KEY)
  } catch {
    // Storage blocked (private mode) — the demo degrades to memory-only.
    return []
  }
  if (raw === null) return []
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    throw new ApiUnavailableError()
  }
  if (!Array.isArray(parsed)) throw new ApiUnavailableError()
  return parsed.map(parseTenant)
}

function writeStore(tenants: TenantSummary[]) {
  try {
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(tenants))
  } catch {
    // Degrade to memory-only; the current response is still correct.
  }
}

function validateRequest(request: CreateTenantRequest, existing: TenantSummary[]): void {
  const fieldErrors: Partial<Record<keyof CreateTenantRequest, string>> = {}

  const name = request.name.trim()
  if (name.length === 0) fieldErrors.name = 'نام مستأجر الزامی است.'
  else if (name.length > 80) fieldErrors.name = 'نام مستأجر نباید بیشتر از ۸۰ نویسه باشد.'

  const slug = normalizeTenantSlug(request.slug)
  if (slug.length === 0) fieldErrors.slug = 'یک شناسه لاتین معتبر وارد کنید (a-z، 0-9، خط تیره).'
  else if (slug.length < 3) fieldErrors.slug = 'شناسه باید حداقل ۳ نویسه باشد.'
  else if (slug.length > 50) fieldErrors.slug = 'شناسه نباید بیشتر از ۵۰ نویسه باشد.'
  else if (!SLUG_PATTERN.test(slug)) fieldErrors.slug = 'شناسه باید با حرف یا رقم شروع و پایان یابد.'
  else if (existing.some((tenant) => tenant.slug === slug)) {
    // Server-side conflict (HTTP 409 in F011), surfaced as its own error.
    throw new TenantConflictError()
  }

  if (request.ownerUserId.trim().length === 0) {
    fieldErrors.ownerUserId = 'مالک نخست مستأجر را انتخاب کنید.'
  }

  if (Object.keys(fieldErrors).length > 0) throw new TenantValidationError(fieldErrors)
}

export const mockTenantAdapter: TenantAdapter = {
  async listTenants() {
    await delay(MOCK_LATENCY_MS)
    return { tenants: readStore() }
  },

  async createTenant(_accessToken, request) {
    await delay(MOCK_LATENCY_MS)
    const existing = readStore()
    validateRequest(request, existing)

    const tenant: TenantSummary = {
      id: crypto.randomUUID(),
      name: request.name.trim(),
      slug: normalizeTenantSlug(request.slug),
      status: 'Active',
      // A tenant always starts with exactly one Owner membership.
      memberCount: 1,
      createdAtUtc: new Date().toISOString(),
    }
    writeStore([...existing, tenant])
    return tenant
  },
}
