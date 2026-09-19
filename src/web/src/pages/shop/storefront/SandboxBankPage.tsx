import { Link, useParams } from 'react-router-dom'
import { loadOrderDraft } from '@/features/shop/orderDraftState'

/**
 * S29 sandbox bank page (F038, mocked): the in-app fake "bank page" the
 * Sandbox payment provider "redirects" to — it stands in for a real gateway's
 * hosted payment page. The visual language is deliberately NOT the
 * storefront's (flat, dashed, neutral, clearly labeled) so nobody mistakes
 * it for a real payment provider's UI. F039 wires Approve/Decline to B032's
 * real callback endpoint.
 */
export function SandboxBankPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const draft = loadOrderDraft()

  return (
    <div className="mx-auto max-w-md rounded-lg border-2 border-dashed border-neutral-400 bg-neutral-100 p-8 text-center text-neutral-900">
      <p className="text-xs font-bold uppercase tracking-widest text-neutral-500">Sandbox Bank — شبیه‌سازی پرداخت</p>
      <p className="mt-4 text-sm">این یک درگاه پرداخت واقعی نیست.</p>
      <p className="mt-2 text-2xl font-bold">{draft?.grandTotal.toLocaleString('fa-IR') ?? '—'} تومان</p>
      <div className="mt-6 flex gap-3">
        <Link
          to={`/shop/${tenantId}/payment-result?outcome=approved`}
          className="flex-1 rounded-md bg-neutral-800 py-2 text-sm font-semibold text-white"
        >
          Approve
        </Link>
        <Link
          to={`/shop/${tenantId}/payment-result?outcome=declined`}
          className="flex-1 rounded-md border border-neutral-400 py-2 text-sm font-semibold"
        >
          Decline
        </Link>
      </div>
    </div>
  )
}
