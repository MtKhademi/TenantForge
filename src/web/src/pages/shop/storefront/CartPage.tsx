import { Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { mockStorefrontCart, type MockCartLine } from '@/features/shop/mockStorefrontCartState'

/**
 * S27 storefront cart (F032 mock, F033 connects to B028's real API).
 * Reads/mutates F030's mockStorefrontCart module directly — the exact
 * same store the product detail page's add-to-cart action populates.
 */
export function CartPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [lines, setLines] = useState<MockCartLine[]>(mockStorefrontCart.getLines())

  useEffect(() => {
    return mockStorefrontCart.subscribe(() => setLines([...mockStorefrontCart.getLines()]))
  }, [])

  const subTotal = mockStorefrontCart.subTotal()

  if (lines.length === 0) {
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

      <div className="divide-y divide-border rounded-xl border border-border bg-surface shadow-soft">
        {lines.map((line) => (
          <div key={line.variantId} className="flex items-center gap-4 p-4">
            <div className="size-16 shrink-0 rounded-md bg-muted" aria-hidden="true" />
            <div className="flex-1">
              <p className="font-semibold">{line.productName}</p>
              <p className="text-sm text-muted-foreground">{line.variantLabel}</p>
            </div>
            <input
              type="number"
              min={1}
              value={line.quantity}
              aria-label={`تعداد ${line.productName}`}
              onChange={(event) => mockStorefrontCart.setQuantity(line.variantId, Math.max(1, Number(event.target.value)))}
              className="w-16 rounded-md border border-input bg-surface px-2 py-1 text-sm"
            />
            <p className="w-24 text-end text-sm font-semibold">
              {(line.unitPrice * line.quantity).toLocaleString('fa-IR')} تومان
            </p>
            <SecondaryButton
              type="button"
              aria-label={`حذف ${line.productName}`}
              onClick={() => mockStorefrontCart.removeLine(line.variantId)}
            >
              <Trash2 aria-hidden="true" className="size-4" />
            </SecondaryButton>
          </div>
        ))}
      </div>

      <div className="flex items-center justify-between rounded-xl border border-border bg-surface p-4 shadow-soft">
        <p className="font-semibold">جمع کل</p>
        <p className="font-semibold">{subTotal.toLocaleString('fa-IR')} تومان</p>
      </div>

      <Link to={`/shop/${tenantId}/checkout`} className="block">
        <Button type="button" className="w-full">ادامه به تسویه حساب</Button>
      </Link>
    </section>
  )
}
