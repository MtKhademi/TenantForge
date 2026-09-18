import { Trash2 } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { cartAdapter } from '@/features/shop/cartAdapter'
import { InsufficientStockError, type CartResponse } from '@/features/shop/cartTypes'

/**
 * S27 storefront cart (F033): connected to B028's real cart API through
 * `cartAdapter`. The cart id is persisted in localStorage (see
 * `cartStorage.ts`), so the same cart reloads after a page refresh or
 * browser restart. Loading is the `undefined` state, empty is `null` or
 * zero items; a stock-insufficient mutation surfaces the API's clear
 * error through the `role="alert"` banner.
 */
export function CartPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [cart, setCart] = useState<CartResponse | null | undefined>(undefined)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    void cartAdapter.getCart(tenantId).then(setCart)
  }, [tenantId])

  useEffect(() => {
    load()
  }, [load])

  if (cart === undefined) return <p>در حال بارگذاری...</p>

  if (cart === null || cart.items.length === 0) {
    return (
      <section aria-label="سبد خرید" className="space-y-4 text-center">
        <p className="text-lg font-semibold">سبد خرید شما خالی است</p>
        <Link to={`/shop/${tenantId}`} className="inline-block">
          <Button type="button">بازگشت به فروشگاه</Button>
        </Link>
      </section>
    )
  }

  return (
    <section aria-label="سبد خرید" className="space-y-6">
      <h1 className="text-2xl font-semibold">سبد خرید</h1>
      {error && <p role="alert" className="text-sm font-semibold text-destructive">{error}</p>}

      <div className="divide-y divide-border rounded-xl border border-border bg-surface shadow-soft">
        {cart.items.map((item) => (
          <div key={item.id} className="flex items-center gap-4 p-4">
            <div className="size-16 shrink-0 rounded-md bg-muted" aria-hidden="true" />
            <div className="flex-1">
              <p className="font-semibold">{item.productName}</p>
              <p className="text-sm text-muted-foreground">{item.variantLabel}</p>
            </div>
            <input
              type="number"
              min={1}
              defaultValue={item.quantity}
              aria-label={`تعداد ${item.productName}`}
              onBlur={async (event) => {
                setError(null)
                try {
                  const updated = await cartAdapter.updateItemQuantity(tenantId, item.id, Math.max(1, Number(event.target.value)))
                  setCart(updated)
                } catch (err) {
                  setError(err instanceof InsufficientStockError ? err.message : 'به‌روزرسانی تعداد ممکن نشد.')
                }
              }}
              className="w-16 rounded-md border border-input bg-surface px-2 py-1 text-sm"
            />
            <p className="w-24 text-end text-sm font-semibold">
              {(item.unitPrice * item.quantity).toLocaleString('fa-IR')} تومان
            </p>
            <SecondaryButton
              type="button"
              aria-label={`حذف ${item.productName}`}
              onClick={async () => {
                const updated = await cartAdapter.removeItem(tenantId, item.id)
                setCart(updated)
              }}
            >
              <Trash2 aria-hidden="true" className="size-4" />
            </SecondaryButton>
          </div>
        ))}
      </div>

      <div className="flex items-center justify-between rounded-xl border border-border bg-surface p-4 shadow-soft">
        <p className="font-semibold">جمع کل</p>
        <p className="font-semibold">{cart.subTotal.toLocaleString('fa-IR')} تومان</p>
      </div>

      <Link to={`/shop/${tenantId}/checkout`} className="block">
        <Button type="button" className="w-full">ادامه به تسویه حساب</Button>
      </Link>
    </section>
  )
}
