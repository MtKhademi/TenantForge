# S22 — Give BuildingBlocks one living ownership guide

## Outcome

`docs/building-blocks/README.md` becomes the read-first knowledge source for
TenantForge BuildingBlocks. An agent can answer what belongs there, what must
stay in a module, who consumes each exported type and whether a proposed change
would reverse the dependency graph.

This guide protects BuildingBlocks from becoming a generic `Common`,
`Shared`, `Utils` or `Helpers` bucket.

## Required knowledge

The guide documents the delivered B018 state:

- project path, assembly, target framework and dependencies;
- API → module → BuildingBlocks dependency direction;
- the complete exported-type catalog;
- `IModuleConfig` ownership, consumers and stable contract;
- `TsidId` generation/parsing/formatting contract and consumers;
- explicit exclusions such as EF converters, pagination, module options,
  entities, DTOs, repositories and feature logic;
- admission and removal criteria;
- compatibility rules for changing a public building block;
- architecture tests and verification commands.

## Admission gate

A type enters BuildingBlocks only when it is a stable cross-module contract or
an accepted system-wide primitive, is meaningful without a business module,
has a named consumer/owner and introduces no reverse dependency. “Might be
reusable later” is not evidence.

Every proposed addition must record:

1. problem and current duplication/dependency pressure;
2. at least two real module consumers, or the accepted system-wide convention;
3. minimal public API;
4. allowed external dependencies;
5. migration/compatibility effect on existing consumers;
6. tests that lock the dependency direction; and
7. why the type cannot remain in its owning module.

If any answer is missing, keep the type in its module.

## Automatic maintenance

After this slice, backend discovery reads the guide for any task touching
`src/building-blocks/**` or changing a BuildingBlocks public contract.
Self-review and PR delivery require exactly one declaration:

```text
BuildingBlocks docs impact: updated — <sections/types>
BuildingBlocks docs impact: none — <specific reason>
```

Review stops when a public type, package, project reference, consumer or
dependency rule changed without updating the guide or providing a defensible
no-impact reason.

## Verification

- The exported-type and consumer tables match the post-B018 code.
- Every referenced path resolves.
- Project references contain no BuildingBlocks → API/module edge.
- A fixed question drill is answerable from the guide without repository-wide
  searching.
- No runtime, database, API or frontend behavior changes in this documentation
  task.

## Acceptance

- The guide is concise, searchable and linked from root/architecture docs.
- Every exported type has a purpose, owner, consumers, dependencies and tests.
- The admission checklist blocks speculative shared abstractions.
- Agent discovery/review automatically enforces documentation impact.
- No secret, task diary or duplicated implementation prose is included.
