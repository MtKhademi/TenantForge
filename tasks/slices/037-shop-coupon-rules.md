# S37 — Coupon rules

## Visible outcome

Minimums, caps and atomic redemption limits.

## Delivered baseline

This slice starts from merged commit `34dc44e` after S31. It extends the existing Shop module and pages; it does not recreate category, product, cart, checkout, order, payment, tracking or permission foundations.

## Tasks

B041, F049, F059.

Each task's executable Spec under `tasks/backend/` or `tasks/front/` is authoritative for class names, routes, error behavior, tests and browser evidence while that task is live. When a task is delivered its Spec is deleted and this slice remains as the product record.

## Boundary

Only the capability named above belongs to this slice. The next slice is not started implicitly. Existing tenant isolation, permission enforcement, Persian RTL UX, guest checkout and order snapshots remain required.
