# CartSync MVP Release and Implementation Plan

Status: accepted release baseline

This specification resolves
[Define release acceptance and implementation slices](https://github.com/EduardoRaygoza/CartSync/issues/10).
It turns CartSync's accepted product, architecture, synchronization, and security
decisions into a staged delivery sequence and a measurable first-release gate.

## Release objective

CartSync's first release is a single-Household production dogfood. It proves
that two Household Members can repeatedly plan and complete real shopping Trips
without losing intent when either member works offline or edits concurrently.

Dogfood is a production stage, not a substitute for release qualification.
CartSync exits dogfood only after the evidence in this document is complete.
Native applications and every item listed as out of scope on the
[Wayfinder map](https://github.com/EduardoRaygoza/CartSync/issues/1) remain out
of scope.

## Branches and environments

`staging` is the integration branch and the source of the isolated staging
environment. Normal feature branches open reviewed pull requests against
`staging`; a successful merge deploys staging automatically.

`main` represents the approved production release. A controlled release pull
request promotes an accepted `staging` tree to `main`. Production deployment
uses the exact immutable artifacts already exercised in staging rather than
rebuilding them from the merge commit. During the explicit deployment window,
`main` represents the release being promoted. Outside that window, `main`
represents what production is running.

Both branches require pull requests and passing checks. Direct commits, force
pushes, branch deletion, and rewritten history are prohibited. A failed
production smoke test causes the operator to:

1. stop the promotion;
2. create a normal revert on `main`;
3. redeploy the previous pinned release manifest; and
4. merge the rollback record back into `staging`.

Production receives only complete vertical slices. Incomplete work may run in
staging, but hidden or disabled production work is not carried behind feature
flags for the MVP.

Staging and production use separate Azure resources, identities, secrets,
databases, and authorization data. Members may independently enter real
shopping data in staging under the same privacy and deletion controls as
production. Production databases, backups, exports, and snapshots are never
copied into staging.

## Release artifacts and promotion

Dogfood versions use SemVer prereleases. One immutable release manifest pins:

- the accepted source commit;
- the Angular build artifact;
- the API container image digest;
- the idempotent database migration bundle;
- the Bicep artifact version; and
- the generated Angular OpenAPI client version.

Each artifact is built once, verified in staging, and promoted by identity.
Database changes run through a separate gated migration job before dependent
application artifacts. The API never runs migrations at startup. Database,
protocol, and browser-storage changes use expand-contract compatibility and
retain the prior supported client behavior defined by the architecture.

Promotion is available on demand after a complete slice passes staging
acceptance. A human approves the pinned release manifest and confirms the
production migration, deployment, and smoke-test results.

## Implementation slices

Each slice remains open until its complete staging journey passes. Pull
requests may deliver smaller reviewable increments, but issue closure represents
working vertical behavior rather than merged components.

### 1. Delivery foundation and walking skeleton

Establish the simple monorepo, Angular and ASP.NET hosts, automated checks,
OpenAPI generation, Bicep environments, workload-federated deployment,
release-manifest production, the separate migration job, redacted telemetry,
and a minimal authenticated request from the PWA through the API to Azure SQL.
Prove staging deployment and the controlled production promotion and rollback
paths before feature delivery depends on them.

### 2. Accounts, Household membership, and trusted installations

Deliver hosted passwordless sign-in, policy acceptance, Household creation,
Member Aliases, Invitations, membership authorization, Trusted and Shared
Device selection, authorization leases, and an empty authorized local
projection. Prove that an Account without a Household can create one, a matching
Account can accept an Invitation, an Account already in another Household is
rejected, and unauthorized or removed members cannot read Household data.

### 3. Stores, Departments, and reusable Store Catalogs

Deliver Store maintenance, configurable Department ordering, Store Products,
Product creation and reuse, normalized-name collision handling, and Store-first
search with Household Product fallback. A new Household must begin with an
empty catalog and explain that state; use must populate the catalog without
seeded production data.

### 4. Trip-first planning, search, quantities, and notes

Deliver the accepted Trip-first interaction: existing Trip Entries remain
primary while a focused full-screen overlay handles search and addition.
Support default `1 each`, all accepted units, decimal amounts, optional
Department placement, live Product-note confirmation, duplicate focus,
Trip Entry removal and Undo, and reuse of Products from Completed Trips.

### 5. Local-first collaboration, in-store mode, completion, and history

Deliver durable IndexedDB projections and pending operations, operation upload
and checkpoint pull, multi-tab coordination, live remote updates, personal Sync
Issues, and every convergence history required by ADR-0002. Deliver the accepted
Next Stop in-store experience, explicit acquisition, progress, last-minute
addition, completion review, carry-forward, Completed Trip correction, and
reusable history.

Planning remains available in a normal supported browser. **Shop this Trip**
requires an installed PWA running in standalone display mode and otherwise
shows platform-appropriate installation guidance.

Completion of this slice is the dogfood-entry gate. It requires two real
members to perform the whole journey in production: sign in, join one
Household, configure a Store, build a Trip from an empty or reusable catalog,
edit concurrently and offline, shop, complete the Trip, and find its Products
and history afterward.

### 6. Security administration and production hardening

Deliver removal, ownership transfer, Account and Household deletion and
restoration, exports, email change, session and installation revocation,
incident notices, retention and purge jobs, break-glass auditing, backup and
restore, operational dashboards, cost controls, and the remaining security
policy acceptance tests. This slice may progress during the dogfood evidence
window but must finish before release qualification can close.

### 7. Human release qualification

Collect the dogfood evidence, perform the manual device and assistive-technology
matrix, run recovery exercises, classify remaining defects, and approve or
reject MVP exit. This work starts after the core journey enters production and
cannot close before the hardening slice.

## Dogfood exit evidence

The MVP exits dogfood only when all of the following are true:

- Two Household Members have used production for two calendar months and
  completed at least four real Trips.
- The evidence includes starting with an empty Store Catalog, reusing Products
  from Completed Trips, searching during Trip creation, quantities, live notes,
  Store-specific Department ordering, concurrent edits, completion, editable
  history, and carry-forward of unpurchased entries.
- An installed PWA remains offline for four continuous hours while a member
  adds, edits, checks, and completes shopping work; it survives force-close and
  relaunch, then synchronizes without lost intent or duplicated visible effects.
- Invitations, member removal, ownership transfer, Account and Household
  deletion and restoration, Trusted Device expiry and revocation, export, and
  required purge behavior pass their accepted security scenarios.
- No critical or high defect remains open. Data loss, a security or privacy
  breach, cross-Household access, an inaccessible core journey, or a core
  journey failure without a safe workaround is always blocking. Lower-severity
  defects require documented impact and disposition.
- Every implementation slice is closed and the promoted release manifest is
  reproducible from retained artifacts.

## Compatibility, accessibility, and performance

The MVP language is English Canada (`en-CA`). User-facing strings are kept out
of business logic and structured for later localization. CartSync follows the
operating system's light or dark preference; both themes must meet the same
accessibility requirements. There is no manual theme setting in the MVP.

Every core journey must conform to WCAG 2.2 AA and pass with:

- keyboard-only operation;
- NVDA with Chrome;
- VoiceOver with Safari on a real iPhone; and
- TalkBack with Chrome on a real Android device.

The supported-browser release matrix is:

- current and previous Chrome and Firefox on desktop;
- current and previous Safari/WebKit on desktop;
- current Chrome on Android; and
- real iPhone and iPad hardware on iOS/iPadOS 17 or newer.

Required performance evidence is:

- local interaction rendering within 100 ms;
- durable optimistic local commit within 250 ms;
- offline relaunch within three seconds; and
- authorized bootstrap within ten seconds for 10 members, 25 Stores, 5,000
  Products, and 1,000 retained Trips.

## Verification strategy

Testing is risk-based; no global line-coverage percentage substitutes for the
required scenarios.

- Vitest and Angular Material harnesses cover browser services, local state,
  interaction behavior, themes, and accessible component contracts.
- Playwright covers supported browsers, installation gating, PWA updates,
  online-only mode, offline relaunch, complete journeys, and automated
  accessibility checks.
- xUnit and property-based tests cover canonical reducers, identity aliasing,
  normalization fixtures, convergence histories, and invariants.
- SQL Server Testcontainers and staging Azure SQL cover transactions,
  `sp_getapplock`, row-level security, pooled `SESSION_CONTEXT`, cursor
  monotonicity, migrations, rollback, and managed-identity boundaries.
- Contract checks cover OpenAPI validity and generated-client drift, operation
  version compatibility, IndexedDB migration preflight, Bicep lint and what-if,
  firewall restrictions, workload federation, and release-manifest integrity.

Release qualification repeats duplicate upload, multi-tab takeover, process
termination, quota exhaustion, storage loss, unavailable scopes, transient
retry, permanent rejection, serverless database resume, Container Apps
scale-to-zero recovery, SSE reconnect, previous-client compatibility, operator
rollback, and backup restoration scenarios.

## Operations and recovery

Dashboards expose redacted availability, errors, synchronization lag, pending
operation age, storage, deployment and migration state, backup state, Azure SQL
free-offer consumption, and projected cost. Product names, notes, Trip contents,
tokens, codes, and exports never appear in operational telemetry.

The operator checks production dashboards every day at 09:00, 13:00, and 18:00
Pacific time. The recovery objective is restoration within eight hours for an
incident detected during the 09:00–18:00 monitoring window. CartSync does not
claim continuous monitoring or a 24/7 recovery objective. Required member
security and incident notifications remain governed by the security policy and
are not replaced by operator dashboards.

Recovery exercises must demonstrate:

- no more than 15 minutes of server-side data loss at the recovery point;
- restoration within the applicable eight-hour window;
- continued access to already synchronized offline shopping data during an API
  outage;
- successful Azure SQL point-in-time restoration and application validation;
  and
- safe application rollback without reversing expand-compatible database
  changes.

The combined staging-and-production Azure target is $25 per month in the
subscription's billing currency. Free grants and scale-to-zero are used where
they do not reduce availability. A forecast above $50 requires an explicit cost
review and recorded disposition; reaching a budget threshold never
automatically pauses or deletes production resources.

## Release decision record

The human qualification issue records links to the four Trips, test runs,
device matrix, accessibility results, performance measurements, recovery
exercise, release manifest, cost review, and defect disposition. It contains no
shopping content or secrets.

Passing every gate authorizes the first non-dogfood MVP release. A failed gate
keeps qualification open and records the owning implementation issue; it does
not weaken or waive an upstream product, convergence, security, or privacy
decision.
