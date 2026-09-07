# S17 — Learn to use TenantForge through one working example

## Owner and user need

F024: ui-engineer, documentation and browser walkthrough only. The project
owner finds the application confusing and needs to know what to do, in which
order and under which account/scope. A developer setup README alone does not
answer those questions.

## Deliverable and ownership

- Create docs/user-guide/README.md: a practical Persian user guide with a short
  table of contents and two independent audience entry points: «راهنمای ادمین
  پلتفرم» and «راهنمای کار با یک مستأجر». Each has a quick start and complete
  page/workflow reference; an owner/member must not need the admin walkthrough
  to understand how to use their already-provisioned tenant.
- Add a prominent «راهنمای استفاده از برنامه» relative link near the beginning
  of the root README.md. F024 is the sole shared-file owner of that small README
  change, after F021 has delivered its setup/copy reconciliation.
- Use concise ordinary Persian, exact delivered button/menu names and short
  numbered steps. English identifiers, emails and URLs remain readable as code.
- Aim for a roughly ten-minute first walkthrough. Put additional explanations
  and troubleshooting after it so a beginner can start without reading a manual.
  This is a quick-start target, not a length limit on either audience's guide.
- The guide describes implemented behavior at the checked-out commit, not task
  plans or intended features. Record the verified commit/date in a small note.
  Link setup instructions rather than copying commands and credentials that
  could diverge. Link agent/developer workflow separately for readers who need it.

## Required content

| Part | Questions it must answer |
|---|---|
| Start here | Where do I open the running app? How do I obtain/use the configured development login? What is the first useful action? |
| Small glossary | What are platform, tenant, account, membership, role, permission and invitation, using one company example? |
| Who can do what | How do platform admin, tenant owner and an ordinary member differ? Which scope does each action require? |
| Where am I | How do the header/switcher and current sidebar item differ? How do I enter a tenant and explicitly return to platform? |
| Accounts and membership | Does creating an account add it to a tenant? What does choosing an Owner during tenant creation do? Does a platform admin automatically belong to every tenant? |
| Roles | How do I create a custom role, select permissions, save and assign it to an existing member? How do membership Owner/Member and configurable permission roles differ? |
| Invitations | How do I register an invitation and choose a role? What do pending, duplicate and expired mean? Does this version send mail or accept invitations? |
| Audit | Where do I see supported recorded changes? How do filters and pagination work? Which action should I perform to produce a visible event? |
| Lists | How do I change pages/page size and reach later options in selectors? What does the filtered total mean? |
| Troubleshooting | Why is a tenant/menu/member missing, why am I denied, why did scope change, and why did an invite not add a member? |

Use a compact action matrix with columns for acting account, current scope,
menu/action and expected result. Do not flatten ownership and permission-based
access into a claim that every ordinary member has the same permissions.

## Mandatory track A: platform administrator

Start with the configured admin login and recognition of platform scope. Cover
every actually delivered platform page and action, including:

1. Dashboard: what each displayed metric represents, its scope, what it helps
   the admin inspect and the meaning of any actual linked action. Do not invent
   tenant-specific analytics for a platform-wide summary.
2. Platform users: listing, supported filters/pagination, creating an account,
   field explanations and validation/conflict recovery. Explain the difference
   between an account and membership; do not promise edit/delete/reset-password
   controls unless they actually exist.
3. Tenants: listing/status/count meanings, creating a tenant, name/slug/Owner
   fields, selecting an Owner from later pages and duplicate/invalid-input
   feedback. Explain the resulting Owner membership and which account to use
   next. Show the case where the creator is not that Owner.
4. Tenant entry and return: how Enter/switcher changes scope, the conditions
   under which an admin can access tenant pages, explicit return to platform
   and the distinction between switching account and switching tenant.
5. Admin daily reference and FAQ: find a user/tenant, understand an empty table,
   locate an off-page Owner, handle an inaccessible tenant and expired login.
   Document only supported actions; list missing capabilities as limitations.

End with an admin completion checklist: the ordinary account exists, Aftab was
created with the intended Owner, and the reader knows how that Owner signs in.

## Mandatory track B: one tenant's owner and members

Start from an existing account and membership. State how those prerequisites
are obtained in the delivered version, with a short link to the admin handoff;
do not tell every member to create a platform account/tenant themselves.

1. Login and orientation: owner versus ordinary-member entry, single/multiple
   memberships, no-membership state, tenant name/header, switcher, current-page
   indicator and access to another authorized tenant. Platform navigation is
   not a required step for a member's daily work.
2. Members: whose records are displayed, actual fields and membership roles,
   paging and the difference from global Users. Explain that creating a global
   account or assigning a role does not by itself add a membership.
3. Roles and permissions: built-in/custom roles, actual view/create/edit/assign/
   unassign actions, permission selection and save, effect on existing members,
   read-only cases and last-administrator restrictions. Clearly distinguish
   membership Owner/Member from permission roles. Include one allowed action and
   one unavailable action for an ordinary member with a stated permission set.
4. Invitations: prerequisites, email/role fields, custom roles, pending list,
   duplicates/expiry, who can view/create, and current email/acceptance limits.
   Do not turn invitation registration into a fictitious completed onboarding.
5. Activity log: who can view it, which supported operations generate events,
   meaning of fields, action/date filters, totals, page navigation and no matches.
6. Daily reference and FAQ: missing menu/tenant, forbidden page, unchanged access
   after role editing, missing member, invitation not received, no audit matches,
   and a disabled Next control. Explain a supported next action in each case.

End with separate owner/member completion checklists. The owner knows how to
configure supported access and invitations; an ordinary member knows how to
identify their scope, use permitted pages and seek help for unavailable actions.

## Required recipe and access matrix for both tracks

For every delivered page, state its purpose, eligible account and permissions,
how to reach it, field meanings, each supported action, expected result and
common empty/error outcomes with a next action. Use actual Persian UI labels.

Include one comparison matrix with columns for action, platform admin, tenant
owner, ordinary member and required scope/permission. Derive access from the
delivered contract and verification, including admin membership requirements;
do not mark every tenant action universally available to a platform admin.
Treat an account with both platform and tenant roles explicitly according to
its current scope. Keep the recipe useful to readers without API knowledge.

## Worked journey: company Aftab

1. Start with a running development instance using the root setup instructions.
   Sign in as its configured platform admin; identify the platform indicator.
2. Create an ordinary account such as Sara using synthetic data and a user-chosen
   development password. Explain that account creation alone grants no tenant
   membership. Do not place real passwords, tokens or personal data in the guide.
3. Create «شرکت آفتاب» with a unique slug such as aftab-demo and choose Sara as
   Owner. Explain what membership the real create flow establishes. Include a
   simple recovery if the example account/slug already exists; no database reset.
4. Explicitly sign out of the admin account and sign in as Sara when owner
   membership is required. Show how to enter Aftab and recognize its scope.
   Do not assume the platform creator also received a membership.
5. Visit members, roles, invitations and audit while remaining in Aftab. Explain
   why platform users and tenant members are different destinations.
6. Create a custom role (for example «مشاهده گزارش‌ها») with the actual delivered
   permissions needed to view audit. Show assignment only to an existing member
   for whom the operation is supported. State what selecting permissions changes
   and that a role without an assignment does not grant access to another person.
7. Register an invitation for another synthetic email using a supported role;
   inspect its pending state and its recorded audit event. Explicitly stop at
   the implemented boundary: never promise email delivery, acceptance or a new
   membership when those flows are unavailable.
8. Demonstrate audit filters and page controls. If the small example has only
   one page, explain disabled controls; use an existing development fixture for
   later-page verification instead of asking the learner to create 51 records.
9. Return to platform using the appropriate admin account/action when needed;
   show how to re-enter the tenant. Explain account changes separately from scope
   changes. Ordinary tenant members cannot elevate themselves via the switcher.

For every step state the account, scope, exact click/field values and visible
result. Do not invent an Add Member screen or use SQL/API calls as hidden steps.
If membership provisioning needed for an optional assignment demo is unavailable,
say so and identify a documented existing fixture; the core quick start must
remain usable without that fixture or an unimplemented invitation-acceptance flow.
Do not promise an Owner loses effective privileges just because a custom role
is changed, or instruct users to remove the last administrator.

## Validation and boundaries

Follow the written quick start literally in a real browser, then follow the
reference sections for roles, denied/empty states and pagination. Record the
actual result of each step and correct wrong labels/order before delivery.
The browser demo includes three independent sessions: platform admin following
track A, tenant owner following track B, and an existing ordinary member
following the member instructions in track B. Record the ordinary member's
fixture and permissions explicitly. Check every delivered page/action against
its guide track and at least one relevant denied/empty state per track. Neither
track is complete merely because the mixed-account Aftab example works.
Review Markdown rendering, internal headings and relative links for readability.

No product implementation, in-app help screen, new backend endpoint, test-policy
change or architecture tutorial. F021 owns the earlier broad documentation/copy
cleanup; F024 adds the practical usage guide after F022 stabilizes the workflow.
Never present task registration as creation or verification of the final guide.
