# Shop payment boundary for S40–S41

## Trust boundary

The browser may request initiation and carry an opaque result token. It never declares payment success, amount, provider reference or order status. A provider callback is a hint; only server-to-server verification may mark an order Paid.

## State flow

`PendingPayment order -> Initiated attempt -> provider redirect -> callback -> server verification -> Succeeded/Paid or Failed/PendingPayment`.

Cancellation invalidates live attempts. A late callback cannot revive a cancelled order. Initiation and callback completion are independently idempotent.

## ZarinPal implementation notes

- Keep merchant ID in configuration/environment, never database responses or logs.
- Request and verify use the official v4 JSON API selected in B045 implementation discovery.
- Currency is explicit (`IRT` or `IRR`) and conversion is checked exactly once.
- Request code 100 creates a redirect authority. Verification code 100 is success. Code 101 is reconciled only against an already stored success/reference; it is not blindly accepted.
- Inquiry, refund and reversal remain outside S41.

The B045 learning note must link the exact official documentation pages used and record their review date because payment contracts can change.
