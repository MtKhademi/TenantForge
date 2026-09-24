import type { AdminOrderStatus } from '@/features/shop/contracts/adminOrdersContract'
import { cn } from '@/lib/utils'

/**
 * S38 (F050): the four admin order statuses, rendered with a distinct, calm
 * tint each (no gradient, no decorative color) and a small status dot. Shared
 * by the list rows and the detail header so the same status always looks the
 * same on both surfaces.
 */
const STATUS_META: Record<AdminOrderStatus, { label: string; tone: string; dot: string }> = {
  PendingPayment: { label: 'در انتظار پرداخت', tone: 'bg-warning/10 text-warning', dot: 'bg-warning' },
  Paid: { label: 'پرداخت‌شده', tone: 'bg-success/10 text-success', dot: 'bg-success' },
  Cancelled: { label: 'لغو‌شده', tone: 'bg-muted text-muted-foreground', dot: 'bg-muted-foreground' },
  Fulfilled: { label: 'تحویل‌شده', tone: 'bg-primary/10 text-primary', dot: 'bg-primary' },
}

export function OrderStatusBadge({ status }: { status: AdminOrderStatus }) {
  const meta = STATUS_META[status]
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        meta.tone,
      )}
    >
      <span aria-hidden="true" className={cn('size-1.5 rounded-full', meta.dot)} />
      {meta.label}
    </span>
  )
}
