# B045 — ZarinPal request and server-side verification

## 1. Files changed and why

- `src/modules/shop/TenantForge.Modules.Shop/features/payments/ZarinPal/*` adds the ZarinPal provider implementation: bound options, amount conversion, provider request/verify parsing, signed callback-state protection, the gateway class and the callback endpoint.
- `PaymentsFeature.cs` now pre-mints a payment-attempt id for ZarinPal, signs the callback state, sends the backend callback URL to the provider and persists the attempt only after initiation succeeds.
- `IShopPaymentGateway.cs` carries the server-stored amount into verification and exposes the optional raw provider verify code so the callback feature can handle ZarinPal `101` safely.
- `ShopPaymentAttempt.cs` can accept a pre-minted TSID when a provider callback state must bind to the attempt before the row exists.
- `ShopConfig.cs` registers the ZarinPal gateway/options/state protector and validates ZarinPal configuration fail-closed.
- `ApiFactory.cs`, `IamDbFixture.cs` and `ShopZarinPalPaymentIntegrationTests.cs` add hermetic Production config plus a dedicated real-PostgreSQL test suite with a fake ZarinPal HTTP handler.
- `docs/modules/SHOP.md`, `docs/design/shop/http-contracts.md`, `docs/knowledge/AGENT-backend.md` and `docs/knowledge/HUMAN-backend.md` now describe the delivered behavior.

## 2. Request flow from endpoint to response

1. The browser calls `POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate` with an `Idempotency-Key`.
2. `PaymentsFeature` locks the order, derives the raw result token, pre-mints an attempt id and signs a ZarinPal callback state containing tenant id, order id, attempt id and the raw token.
3. `ZarinPalPaymentGateway.InitiateAsync` sends merchant id, checked integer amount, currency, description and the backend callback URL to the configured request endpoint.
4. On request code `100` with a non-empty authority, the attempt is persisted with that authority in `GatewayReference`, and the browser receives a ZarinPal redirect URL plus the opaque result token.
5. ZarinPal redirects the shopper to `GET /api/shop/{tenantId}/payments/zarinpal/callback?Authority=&Status=&state=`.
6. `ZarinPalCallbackFeature` trusts only the signed state. `Status != OK` resolves the attempt as declined without a verify call. `Status == OK` calls `VerifyAsync` using the stored authority and stored amount.
7. The callback feature passes the resulting `GatewayVerification` into `ShopPaymentCompletionService`, the only class allowed to move attempts or mark orders paid.
8. The callback returns `302` to the configured frontend result route with only route context, `outcome` and the opaque token.

## 3. Backend concepts introduced

- **Provider-specific implementation behind a neutral seam:** ZarinPal is another `IShopPaymentGateway`; provider choice still comes from `Shop:Payments:Provider`.
- **Signed callback state:** ASP.NET Core Data Protection protects an expiring token so the callback can recover server-authored context without trusting browser query values.
- **Server-to-server verification:** the browser return is never enough to pay an order; the backend verifies authority and amount with the provider.
- **Fail-closed startup validation:** ZarinPal configuration is checked at activation, and unsafe Production URLs fail startup.
- **Provider outage vs payment outcome:** a timeout or malformed provider response returns `503` and leaves the attempt `Initiated` rather than marking it paid or failed.

## 4. Important security decisions

- Merchant id is configuration-only and is never returned, logged or persisted per order.
- Amount, order id and payment success are not accepted from the browser; they are derived from signed state and stored rows.
- Gateway authorities, raw callback tokens and card PANs are not logged.
- ZarinPal code `101` is not blindly accepted. It is success only when it matches a success reference already stored for the same attempt; otherwise it fails closed.
- The sandbox provider remains Development-only and is refused outside Development.

## 5. Alternatives deliberately postponed

- No refunds, inquiry scheduler, webhook, split payment, fee calculation or multiple merchant accounts.
- No new EF migration: B044's existing `GatewayReference`, `ProviderReference`, `FailureCode`, `AmountSnapshot` and verification fields cover this slice.
- No generic payment lifecycle framework beyond the existing gateway seam.
- No frontend changes in this backend task.

## 6. Commands and manual steps to verify

Automated commands run from repository root:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test TenantForge.sln --nologo --filter FullyQualifiedName~ShopZarinPalPaymentIntegrationTests
dotnet.exe test TenantForge.sln --nologo
```

Manual/browser handoff for F062:

1. Configure `Shop:Payments:Provider=ZarinPal` and valid ZarinPal URLs/merchant id.
2. Create a pending Shop order through the storefront.
3. Initiate payment and assert the returned `redirectUrl` host matches the configured `GatewayBaseUrl` host.
4. Complete the provider flow; the callback should redirect to `/shop/{tenantId}/payment-result` with `outcome=approved` on verified success or `outcome=declined` on decline/failure.
5. Use the opaque result token on the status route to confirm the final server-side order status.

## 7. ZarinPal documentation note

The implementation targets ZarinPal's v4 request/verify JSON envelope (`payment/request` and `payment/verify`) with request success code `100`, verify success code `100` and already-verified code `101`.

Intended official documentation URLs:

- `https://www.zarinpal.com/docs/paymentGateway/`
- `https://docs.zarinpal.com/paymentGateway/`
- `https://dev.zarinpal.com/docs/paymentGateway/`

Review date for this task: 2026-09-24. Network egress from this execution environment was blocked/failed when attempting to fetch ZarinPal documentation, so the exact live pages could not be independently retrieved here. The provider contract was implemented from the task Spec's required ZarinPal behavior and covered with fake-provider integration tests.

## 8. Review questions for the learner

1. Why does the callback route ignore the browser's `Authority` value and use the stored authority from the attempt instead?
2. Why does a provider timeout leave an attempt `Initiated` instead of marking it `Failed`?
3. What would be unsafe about accepting ZarinPal verify code `101` without comparing the returned reference to a reference already stored for that attempt?
