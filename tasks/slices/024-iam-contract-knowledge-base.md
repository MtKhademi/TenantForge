# S24 — Give the IAM Contract project one living ownership guide

## Outcome

`docs/contracts/iam.md` becomes the current, searchable handbook for
`TenantForge.Modules.Iam.Contract`, in exactly the shape
`docs/building-blocks/README.md` (B020/S22) established for
`TenantForge.BuildingBlocks`: purpose and strict non-purpose, fast facts,
dependency rule, an exported-type catalog (all 32 types from S23), the
admission checklist future tasks must complete before adding a new
contract type, the explicit exclusion list, a change/compatibility policy,
a test map, decision records and a change-impact checklist.

This task follows B021–B023 (S23) because it documents the final,
delivered namespace layout and type roster rather than an in-flight one —
the same ordering B020 used after B018.

## Scope

- Write `docs/contracts/iam.md` covering every type moved in S23, its
  owning namespace/folder, its current consumer (today: only
  `TenantForge.Modules.Iam`'s own feature files — there is no second module
  yet, and the guide must say so plainly rather than inventing a consumer),
  and the admission rule from `tasks/slices/023-iam-contract-separation.md`.
- Add a "Living IAM Contract knowledge" section to `AGENTS.md`, mirroring
  the existing "Living BuildingBlocks knowledge" section: any task that
  touches `src/modules/iam/TenantForge.Modules.Iam.Contract/**` or a
  consumer's reference to it reads `docs/contracts/iam.md` first, then
  classifies its diff against the guide's change-impact checklist before
  review — update the guide, or record
  `IAM Contract docs impact: none — <specific reason>`.
- Cross-link `docs/contracts/iam.md` and `docs/modules/IAM.md` in both
  directions (IAM.md's source-code map already lists the Contract project
  path from B021; add a one-line pointer to the new handbook there).
- Update `docs/architecture.md` only if it currently lists project
  boundaries in a way this new project changes (check before editing —
  most likely a one-line addition next to the existing BuildingBlocks
  mention, not a rewrite).
- Do not add a new contract type, change any endpoint, or alter any test
  behavior. This task changes documentation and agent workflow only,
  exactly like B019/S21 and B020/S22 did.

## Non-goals

- No code in `src/**` changes.
- No new admission decision is made — the guide records the admission
  rule and the current roster, it does not relax or extend it.
- Do not invent a second module or a hypothetical consumer to make the
  guide feel less empty; state plainly that IAM's own feature files are
  the only current consumer.

## Acceptance

- `docs/contracts/iam.md` exists, follows the section shape described
  above, and every fact in it (namespaces, file paths, type list, test
  names) is verified against the code delivered by B021–B023, not copied
  from the S23 slice without re-checking.
- The exported-type catalog lists exactly the 32 types from S23, matching
  `IamContractArchitectureTests`'s exact-match assertion.
- `AGENTS.md` gains the "Living IAM Contract knowledge" section, and its
  wording follows the existing "Living BuildingBlocks knowledge" section's
  structure (read-first rule, change-impact classification, the exact
  `impact: updated` / `impact: none — <reason>` declaration pair).
- `docs/modules/IAM.md` gets its one-line cross-link; no other IAM.md fact
  changes (state `IAM.md impact: none — <reason>` if nothing else in this
  task touches a documented IAM fact).

## Verification

- No automated test is added or changed by this task; the existing full
  integration suite must still pass unmodified (proof that no code moved).
- Manual check: every file path and type name written into
  `docs/contracts/iam.md` is confirmed to exist with `grep`/`find` against
  the actual repository state at review time, not against the S23 slice
  text alone.
