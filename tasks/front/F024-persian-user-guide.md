---
id: F024
slice: S17
title: Write a practical Persian user-guide README
agent: ui-engineer
source: tasks/slices/017-persian-user-guide.md
---

# Objective

A first-time reader can use the running application through two complete Persian
README tracks: platform administration and working inside one tenant. Each track
stands on its own, with its own starting account, quick start, page reference,
daily workflows and troubleshooting. Brevity of the quick start must not remove
coverage of either area.

## Context

Read tasks/slices/017-persian-user-guide.md in full, README.md and
src/web/README.md. Read current S11/S12/S13/S15 design contracts and the S16
navigation source for the specific access, invitation and pagination behavior
being explained. Verify the delivered UI rather than copying planned labels.
F022 must be done; F021 and F023 are already transitive prerequisites.

## Scope and files

- Author docs/user-guide/README.md in simple Persian, with a concise contents
  list and two prominent entry links: «راهنمای ادمین پلتفرم» and
  «راهنمای کار با یک مستأجر». Include a roughly ten-minute quick start followed
  by complete reference/FAQ coverage for each audience; do not require a tenant
  reader to work through the administrator's setup steps.
- Sole shared-file owner of a small root README.md edit adding a prominent
  relative «راهنمای استفاده از برنامه» link. Preserve setup/workflow sections.
- Cover every row of the S17 required-content table and follow its Aftab worked
  journey. Each step names the acting account, scope, exact controls/fields and
  expected screen/result. Use synthetic data and no committed credentials.
- Fulfil both mandatory audience tracks and the per-page recipe in S17. The
  admin track covers the actual platform dashboard, global users, tenants and
  owner selection, scope transitions, filters/paging and admin troubleshooting.
  The tenant track covers owner and ordinary-member entry, members, roles and
  assignments, invitations, audit, scoped navigation and tenant troubleshooting.
- Add a three-audience matrix for platform admin, tenant owner and ordinary
  member, showing prerequisites/permissions and actual scope for each supported
  action. Separate admin-with-membership from admin-without-membership whenever
  their tenant access differs. Do not imply a universal platform-admin bypass.
- Include a compact action/access matrix and distinguish account creation,
  membership, tenant ownership and assigned permission roles. Explicitly explain
  why global users differ from tenant members and how to recognize the scope.
- Document actual role assignment and last-administrator restrictions. Show
  invitation registration and its real limitations; never claim it completes
  registration, sends mail or establishes membership unless delivered code does.
- Include audit filters, table page controls and later-page selector navigation,
  plus useful no-data/no-membership/forbidden/session-expired explanations.
- Link current setup and development workflow guidance without duplicating it.
  Keep task IDs/API internals out of the beginner journey. Add a small verified
  commit/date note; do not create a competing task-status table.
- No application code, dependencies, backend, migration, mock, new help screen
  or frontend test inspection/editing/execution. Do not commit generated browser
  evidence. If screenshots are unnecessary, use precise steps and tables.

## Acceptance and browser demo

- A reader can find the guide immediately from the root README and understand
  the next click without reading source code, endpoint docs or task Specs.
- Execute the core Aftab journey exactly as written on the real API, using the
  correct admin/owner login transitions. Verify the created Owner's membership;
  do not assume the platform creator is automatically a member.
- Verify three independent reader sessions: a platform admin follows only the
  admin track; Sara follows the tenant-owner track after documented provisioning;
  an existing ordinary member follows the member instructions using documented
  fixture access. Each can identify the account/scope, next action and expected
  result without referring to the other audience's walkthrough. Keep any
  unavailable member-provisioning step explicit, not a fabricated UI operation.
- Check every delivered page and supported action against the appropriate
  track. Record separate admin/owner/member evidence and at least one relevant
  denied or empty state per track. Verify the matrix against actual behavior;
  hiding a menu alone is not evidence of a server permission rule.
- Role creation and assignment instructions use existing UI and existing
  members. If an extra-member fixture is needed, name its prerequisite and keep
  it optional; do not invent an invitation acceptance or Add Member flow.
- Verify pending/duplicate invitation, a resulting audit event, audit filtering,
  pagination boundaries and at least one permission-denied/empty state. Keep
  these checks safe for the local fixture and preserve an authorized owner.
- Explain common confusion directly: missing tenant/menu, account absent from
  members, role not changing access, Users changing scope, invite not arriving,
  and Next disabled on a one-page list. Give a supported next action for each.
- Confirm labels and steps on desktop/mobile Persian RTL. Review the rendered
  Markdown, table readability and every internal/relative link. No broken image
  references, real personal data or unverified feature claims.
- Supply a concise per-step verification summary in the delivery review. Mark
  environment-blocked steps honestly; do not call an untested guide verified.

## Verification

Validate relative Markdown paths and heading anchors using the repository's
existing documentation tooling if present; otherwise perform a direct link and
render review without installing packages. Perform the real-browser walkthrough
above. Only Markdown is authored, so application build/lint/test runs are not
needed for this documentation change. The ui-engineer frontend-test restriction
remains in force. Stop and report implementation defects rather than silently
expanding this task to fix them.

## Lifecycle

Status and dependencies live only in tasks/TASKS.md. Follow the owning command,
plan approval and final review gates. After final delivery approval, mark only
F024 done, replace its Spec with — and delete this exact executable Spec in the
same commit. Preserve the user guide, permanent source and root README link.
