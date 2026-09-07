# S17 — Learn to use TenantForge through one working example

## Owner and user need

F024: ui-engineer, documentation and browser walkthrough only. The project
owner finds the application confusing and needs to know what to do, in which
order and under which account/scope. A developer setup README alone does not
answer those questions.

## Deliverable and ownership

- Create docs/user-guide/README.md: a practical Persian user guide with a short
  table of contents, an immediate quick start and progressive reference sections.
- Add a prominent «راهنمای استفاده از برنامه» relative link near the beginning
  of the root README.md. F024 is the sole shared-file owner of that small README
  change, after F021 has delivered its setup/copy reconciliation.
- Use concise ordinary Persian, exact delivered button/menu names and short
  numbered steps. English identifiers, emails and URLs remain readable as code.
- Aim for a roughly ten-minute first walkthrough. Put additional explanations
  and troubleshooting after it so a beginner can start without reading a manual.
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
The browser demo is a newcomer using the README to complete the example.
Review Markdown rendering, internal headings and relative links for readability.

No product implementation, in-app help screen, new backend endpoint, test-policy
change or architecture tutorial. F021 owns the earlier broad documentation/copy
cleanup; F024 adds the practical usage guide after F022 stabilizes the workflow.
Never present task registration as creation or verification of the final guide.
