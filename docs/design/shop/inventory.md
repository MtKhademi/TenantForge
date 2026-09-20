# Shop inventory invariants for carts and orders

The delivered B028 design subtracts variant `StockQuantity` when an item enters a cart and adds it back when the item is reduced/removed. The value therefore means currently available stock, not physical on-hand stock.

S36 treats that subtraction as a time-bounded reservation:

1. Active cart mutation obtains available stock with the existing guarded SQL update.
2. The server extends `ExpiresAtUtc` after a successful mutation.
3. Expiry atomically marks the cart Expired, restores every reserved quantity once and removes its lines.
4. Order creation and expiry lock the same cart. Order creation marks Converted and consumes lines without another stock decrement.
5. Cancellation in S39 restores order-item quantities once and records `InventoryReleasedAtUtc` in the same transaction.

There is no separate inventory ledger in this roadmap. If later requirements need purchase receipts, shrinkage, warehouses or reconciliation, that work must first redefine `StockQuantity` and migrate existing semantics explicitly.
