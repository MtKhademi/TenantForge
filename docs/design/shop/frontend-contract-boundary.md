# Shop frontend mock-to-HTTP contract boundary

## Delivery order

Shop frontend work is intentionally split into two uninterrupted phases:

1. F044–F053 build every new screen/state with deterministic mock clients so the product owner can review the complete UI and flow.
2. F054–F063 bind those accepted client interfaces to delivered backend HTTP contracts. Connection tasks do not redesign pages.

## One contract, two client implementations

Each capability owns:

```text
features/shop/
├── contracts/<capability>Contract.ts   # TS types + Zod wire schemas
├── clients/<Capability>Client.ts       # interface consumed by UI
├── clients/mock<Capability>Client.ts   # mock phase implementation
└── clients/http<Capability>Client.ts   # connection phase implementation
```

`ShopClientsProvider` is the composition seam. During the mock phase every delivered slot is mock-backed. Connection tasks replace exactly one slot at a time. Components receive the same interface and parsed result in both phases.

## Contract rules

- The paired B-task Spec, the persistent
  [`http-contracts.md`](http-contracts.md) section and later its delivered C#
  records/integration tests are authoritative. Backend delivery updates the
  persistent section before its executable Spec is deleted.
- TypeScript preserves JSON member names, nullability, enum strings, pagination, TSID strings, ISO UTC timestamps and RFC7807 reason codes exactly.
- Zod parses external success payloads. A shared `ShopClientError` parses non-success responses. No `any`, unchecked cast or component-owned `fetch` is allowed.
- Mock fixtures satisfy the schemas and use canonical-looking values. UI-only labels/derived values live in view models, never in wire types.
- A real adapter failure never falls back to mock data. A contract mismatch blocks the connection task instead of causing a broad UI refactor.

## Review gate

Every mock task reports `Data source: mock`. Every connection task replays the accepted mock scenarios against the real backend and reports which provider slot became real. After F063, production composition contains no mock Shop client.
