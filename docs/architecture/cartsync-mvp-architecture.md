# CartSync MVP Application Architecture

Status: accepted architecture baseline

This specification defines the build boundary selected by
[Choose the application architecture and interfaces](https://github.com/EduardoRaygoza/CartSync/issues/11).
It implements the domain model in `CONTEXT.md` and the convergence rules in
ADR-0002. The accepted security policy from
[Define security, privacy, and account recovery](https://github.com/EduardoRaygoza/CartSync/issues/8)
is specified in
[`docs/security/mvp-security-privacy-and-recovery.md`](../security/mvp-security-privacy-and-recovery.md).

## System shape

```text
Angular 22 PWA
  ├─ application shell: Angular service worker
  ├─ canonical local projections: native IndexedDB
  ├─ pending typed operations: native IndexedDB
  └─ HTTPS + SSE
          │
          ▼
.NET 10 ASP.NET Core modular monolith
  ├─ API controllers under /api/v1
  ├─ authentication and application authorization
  ├─ canonical operation reducer
  ├─ projection and change-feed queries
  └─ EF Core 10 + Microsoft.Data.SqlClient
          │
          ▼
Azure SQL Database
  ├─ canonical relational state
  ├─ field versions, aliases, and tombstones
  ├─ personal Sync Issues
  ├─ Household cursors
  └─ 180-day metadata-only operation receipts
```

The Angular application is hosted by Azure Static Web Apps. The API runs in an
Azure Container Apps consumption environment. Azure SQL Database is the sole
authoritative and synchronization-supporting server database. All three
resources are deployed in Canada Central where the service supports it.

The browser and API use separate HTTPS origins. The API allows only the exact
PWA origin through CORS. SSE connects directly to the API; it is not proxied
through Static Web Apps.

## Module boundaries

The API is one deployable modular monolith with one domain context. Modules
organize code and ownership without adding network boundaries:

- **Identity access** adapts the authentication provider and establishes the
  current account, Household, member, and owner claims.
- **Household membership** implements connectivity-required invitation,
  removal, ownership-transfer, and deletion commands.
- **Shopping** owns Stores, Products, Store Products, Departments, Trips, and
  Trip Entries.
- **Synchronization** validates operation envelopes, runs the canonical
  reducer, persists receipts and changes, and projects personal Sync Issues.
- **History** serves Completed Trips outside the automatic 90-day local window.

Modules communicate in process through application interfaces. They share one
Azure SQL database but do not bypass another module's application boundary.

## Browser persistence

Normal shopping screens read only from local canonical projections exposed by
Angular services and signals. They do not wait for online queries.

One versioned IndexedDB database contains stores for:

- canonical projections;
- pending operations;
- scope checkpoints;
- identity aliases;
- personal Sync Issues;
- installation identity and monotonic device sequence;
- uploader leases; and
- schema and migration state.

Each optimistic shopping action writes its changed projection and typed
operation in one IndexedDB `readwrite` transaction. The UI reflects the new
projection only after that transaction commits. A failure leaves neither half
visible.

An installation owns a persistent random installation ID and a monotonic device
sequence. A short IndexedDB lease elects one uploader across tabs.
BroadcastChannel invalidates peer-tab views and prompts takeover after lease
expiry. Pending operations survive process termination and retry at startup,
focus, reconnection, and after a successful authentication refresh. Background
Sync may optimize retries but is never required for correctness.

If IndexedDB cannot prove durable writes, CartSync enters explicit online-only
mode. It never presents an unavailable synchronization scope as an empty
catalog, Trip, or history.

Angular's service worker caches only the versioned application shell. Domain
data remains in IndexedDB and is migrated by the application.

## Local data window

The automatic synchronization scope contains:

- Stores;
- Products and Store Products;
- Departments;
- Planned Trips and their Trip Entries;
- the current member's Sync Issues; and
- Completed Trips from the latest 90 days.

Older Completed Trips are loaded through a pageable online endpoint and retained
for the current browser session only. A recorded terminal bootstrap cursor marks
the synchronized scope as available. Until that cursor exists, the UI shows the
scope as unavailable rather than empty.

## Operation envelope

Every mutable local intent becomes a versioned composite operation containing:

- a random UUIDv4 operation ID;
- operation version;
- persistent installation ID;
- monotonic device sequence;
- last applied Household cursor;
- typed operation kind; and
- kind-specific payload.

Composite operations match user intent. They cover Store and Product
maintenance, Store Product placement, Department maintenance and relative
movement, Trip lifecycle, Trip Entry changes, Product notes, Completed Trip
corrections, carry-forward choices, and Sync Issue resolution.

The server processes a batch in device-sequence order. Each operation receives
one independent outcome:

- `accepted` — the intent changed canonical state;
- `coalesced` — the intent maps to an existing canonical identity or loses a
  deterministic field comparison without requiring attention;
- `already_applied` — the operation receipt proves idempotent replay; or
- `needs_attention` — the intent cannot safely join canonical state and has a
  member-specific Sync Issue.

A failure does not prevent later independent operations in the batch from being
processed. Dependent operations may reference a client identity that the server
coalesces; responses and change pulls return the alias to its canonical identity.

## Canonical processing transaction

For each accepted batch, the API:

1. Authenticates the account and authorizes current Household membership.
2. Opens an Azure SQL transaction.
3. Sets `member_id` and `household_id` through `SESSION_CONTEXT` for the
   transaction's private connection scope.
4. Acquires a transaction-owned exclusive `sp_getapplock` for the Household.
5. Treats negative timeout, cancellation, and deadlock return codes as failures;
   no reducer work continues without the lock.
6. Reads existing operation receipts and processes unseen operations in device
   order through the typed canonical reducer.
7. Validates domain invariants and application authorization.
8. Writes canonical relational changes, convergence metadata, aliases,
   tombstones, and personal Sync Issues.
9. Advances the Household cursor once per visible canonical change and records
   metadata-only operation receipts.
10. Commits, then clears session context before the connection returns to the
    pool. A cleanup failure clears the pool rather than risking Household data
    leakage.

Application authorization and Azure SQL row-level security are both mandatory.
RLS is defense in depth, not a replacement for command authorization.

EF Core 10 with `UseAzureSql` handles ordinary projections, queries, and
migrations. `Microsoft.Data.SqlClient` and explicit T-SQL transactions handle
application locks, the canonical reducer boundary, cursor allocation, and other
convergence-critical writes.

## Identity and relational persistence

Public domain identities are client-generated UUIDv7 values. Azure SQL maps each
to an unexposed `bigint IDENTITY` clustered key used by internal foreign keys.
Operation IDs remain random UUIDv4 values. UUID timestamps never determine
causal or conflict order.

Normalized name keys use Unicode NFKC, trimmed and collapsed whitespace, and
invariant case folding while preserving accents and punctuation. They are stored
under a binary collation and protected by Household- or Store-scoped unique
indexes. JavaScript and .NET run the same conformance fixtures.

Canonical storage includes current entity rows, per-field versions, relative
Department movement metadata, identity aliases, removal/archive tombstones,
personal Sync Issues, Household cursors, and operation receipts. Receipts retain
operation ID, member, installation, sequence, outcome, cursor, and timestamps
for 180 days; they do not retain payloads and do not form a permanent audit log.

## Convergence ownership

The browser applies local intent optimistically but never decides canonical
conflicts. Incoming canonical projections are applied first, then still-pending
local operations are replayed over them to rebase the visible local projection.

The .NET reducer exclusively implements ADR-0002:

- independent fields merge;
- causal order decides same-field actions when known;
- stable operation metadata, never device clocks, breaks true ties;
- removal wins over unseen edits while preserving losing intent as a Sync Issue;
- normalized duplicate identities coalesce and emit aliases;
- Department relative moves preserve unaffected order;
- first completion fixes the Completed Trip snapshot;
- later valid entry edits become historical corrections;
- competing later carry-forward choices become Sync Issues;
- archived Product references remain valid without restoring the Product; and
- new Trips for archived Stores become Sync Issues.

## Synchronization interfaces

The machine-readable contract is
[`docs/api/openapi.yaml`](../api/openapi.yaml). Its stable versioned boundary is:

- `POST /api/v1/sync/operations` uploads ordered operation batches and returns
  independent outcomes.
- `GET /api/v1/sync/bootstrap` pages the authorized automatic scope and ends
  with a terminal Household cursor.
- `GET /api/v1/sync/changes?after=…` returns canonical deltas, field versions,
  aliases, tombstones, attribution, personal Sync Issues, and the next cursor.
- `GET /api/v1/sync/events` emits SSE checkpoint and affected-scope hints.
  Checkpoint pull remains authoritative.
- `GET /api/v1/completed-trips` pages history older than the automatic window.
- `/api/v1/household/*` exposes connectivity-required membership commands.

ASP.NET generates the OpenAPI 3.1 document. CI validates the checked-in contract
and generates the Angular client with the pinned `typescript-angular`
configuration. Generated client drift fails CI.

## Status and failure presentation

Each shopping surface derives one of four ordinary states from durable local and
server evidence:

- **Offline** — durable local changes are allowed, but the API is unreachable.
- **Saving** — one or more durable local operations await acknowledgement or the
  client is applying canonical changes.
- **Synced** — eligible operations are acknowledged and the local checkpoint is
  current.
- **Needs attention** — one or more personal Sync Issues require a decision;
  unrelated synchronization continues.

Remote changes arrive through checkpoint pulls, optionally prompted by SSE, and
show subtle member attribution without locks or presence requirements.

## Versioning and migration

Database, protocol, and PWA releases use expand-contract changes. The server
retains old operation handlers for at least 90 days. A new PWA shell activates
only after migration preflight proves every pending operation can be preserved,
upgraded, or converted into a personal Sync Issue.

An installation migration is restartable and records its stage. Quota exhaustion
or an unrecoverable browser-storage loss moves the installation to an explicit
recovery state and never silently discards pending intent.

## Deployment baseline

Infrastructure is declared in Bicep and deployed through GitHub Actions using
OIDC workload federation; no Azure credential is stored in GitHub.

Azure SQL uses the General Purpose serverless free offer with “continue using
with additional charges” enabled. Availability is preferred over pausing at the
free limit. The MVP target is USD 25 per month, protected by Azure Cost
Management and remaining-vCore alerts.

Container Apps connects to Azure SQL over TLS through a restricted public
endpoint using a user-assigned managed identity and least-privilege contained
database roles. Explicit firewall rules are required. Broad “Allow Azure
services” access is disabled.

Private Link, multi-region failover, detailed alert thresholds, and production
backup-retention policy are deferred beyond the MVP architecture decision.

## Release evidence obligations

Implementation is not releasable until automated histories cover every ADR-0002
case, RLS and pooled-connection isolation, application-lock failures, duplicate
uploads, multi-tab takeover, termination, quota and storage loss, schema
migration, Azure SQL auto-resume, Container Apps scale-to-zero, SSE reconnect,
OpenAPI drift, Bicep validation, managed identity, firewall restrictions, and
OIDC deployment.

Performance gates are:

- local rendering within 100 ms;
- durable local commit within 250 ms;
- offline relaunch within three seconds; and
- large-Household bootstrap within ten seconds for 10 members, 25 Stores, 5,000
  Products, and 1,000 retained Trips.

Supported-browser evidence must cover current and previous Chrome, Firefox, and
Safari/WebKit releases plus real iPhone and iPad hardware on iOS/iPadOS 17 or
newer.
