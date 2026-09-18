/**
 * S26 storefront (F030 mock, F032 builds the cart page on top of this).
 * A module-scoped store with a subscribe/notify pattern so the cart-icon
 * count in StorefrontLayout (and the future cart page) re-render on change,
 * without introducing a new state library.
 */

export type MockCartLine = {
  variantId: string
  productName: string
  variantLabel: string
  unitPrice: number
  quantity: number
  imageUrl: string
}

type Listener = () => void

const lines: MockCartLine[] = []
const listeners = new Set<Listener>()

function notify() {
  for (const listener of listeners) listener()
}

export const mockStorefrontCart = {
  getLines(): MockCartLine[] {
    return lines
  },
  addLine(line: MockCartLine) {
    const existing = lines.find((l) => l.variantId === line.variantId)
    if (existing) existing.quantity += line.quantity
    else lines.push(line)
    notify()
  },
  setQuantity(variantId: string, quantity: number) {
    const line = lines.find((l) => l.variantId === variantId)
    if (line) line.quantity = quantity
    notify()
  },
  removeLine(variantId: string) {
    const index = lines.findIndex((l) => l.variantId === variantId)
    if (index !== -1) lines.splice(index, 1)
    notify()
  },
  subscribe(listener: Listener): () => void {
    listeners.add(listener)
    return () => listeners.delete(listener)
  },
  itemCount(): number {
    return lines.reduce((sum, l) => sum + l.quantity, 0)
  },
  subTotal(): number {
    return lines.reduce((sum, l) => sum + l.unitPrice * l.quantity, 0)
  },
}
