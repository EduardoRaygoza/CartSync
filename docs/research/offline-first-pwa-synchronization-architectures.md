# Offline-First PWA Synchronization Architectures

Research date: 2026-10-02

## Executive summary

CartSync needs more than an offline cache. It needs durable local intent, causal
and deterministic convergence, server-enforced household rules, and a personal
place to preserve actions that cannot enter canonical household state. None of
the evaluated products implements that complete policy.

The strongest architecture candidates for the next decision are:

1. **A custom IndexedDB operation log with a server-authoritative Postgres
   operation processor.** This is the best semantic and security fit and the
   lowest-lock-in baseline, but it owns the most synchronization engineering.
2. **PowerSync plus the same application operation processor.** PowerSync can
   supply durable local SQLite, an ordered upload queue, partial download sync,
   and causal checkpoints. CartSync still owns its operation envelope and all
   domain convergence rules, and must prove the Web SDK on mobile Safari.
3. **RxDB replication plus a custom Postgres backend.** RxDB supplies a mature
   local reactive database, retrying replication, and multi-tab coordination,
   but its native document-state conflict model does not replace CartSync's
   operation-level reducer. Production browser storage may also introduce a
   paid license.

ElectricSQL is a credible read-path component but is not a complete candidate
because its current product explicitly omits write-path synchronization.
Firestore, CouchDB/PouchDB, Automerge, and Yjs all provide useful capabilities,
but their native conflict, validation, or tenancy models leave more mismatch
than advantage for this domain.

This report ranks evidence for the architecture decision; it does **not** select
CartSync's application architecture or define APIs, schemas, or public types.

All linked sources are first-party documentation, specifications, source
repositories, release pages, or pricing pages and were accessed on 2026-10-02.

## Decision constraints

The confirmed collaboration semantics require the selected architecture to
support all of the following together:

- previously synchronized shopping data remains writable offline and pending
  changes survive browser restarts;
- independent fields merge, while same-field edits and check/uncheck actions
  use causal order and a stable non-clock tie-break;
- removal wins over unseen edits while preserving the losing member's intent as
  a personal Sync Issue;
- concurrent normalized duplicates coalesce into one canonical identity;
- concurrent Department moves converge without disturbing unaffected relative
  order;
- the first Trip completion fixes the Completed Trip snapshot, while late edits
  become corrections and later carry-forward choices become Sync Issues;
- archived Product references can remain valid without restoration, but new
  Trips for archived Stores are rejected into Sync Issues;
- transient failures retry automatically, while permanent validation and
  authorization failures become actionable without blocking unrelated work;
- data never synchronized to the device is shown as unavailable, not empty;
- remote changes appear live without locks; and
- household isolation, authentication, and server-side authorization remain
  enforceable even though clients are offline and therefore untrusted.

These are application semantics. A vendor's claim of "offline-first," "CRDT,"
or "conflict resolution" is insufficient unless the application can express
these exact rules and observe rejected intent.

## Browser baseline and non-negotiable PWA safeguards

- IndexedDB is a transactional store intended for offline web applications.
  Transactions are atomic and expose `strict`, `relaxed`, and `default`
  durability hints; `strict` asks the browser to verify persistence but remains
  a hint. Ordering is guaranteed within one transaction, not across unrelated
  transactions. Each user action and its outbound operation therefore needs
  one local transaction boundary. ([IndexedDB 3.0 specification](https://www.w3.org/TR/IndexedDB/), accessed 2026-10-02)
- Browser storage is not equivalent to guaranteed disk ownership. WebKit's
  default mode is best effort and can be evicted under storage pressure or LRU
  policy; Safari 17 and iOS 17 added the Storage API and larger quotas, and a
  Home Screen web app is eligible for browser-app quotas and persistent-storage
  heuristics. The app must request persistence, monitor quota, handle
  `QuotaExceededError`, and explain that clearing site data removes unsynced
  work. ([WebKit storage policy](https://webkit.org/blog/14403/updates-to-storage-policy/), accessed 2026-10-02)
- WebKit's tracking-prevention policy can delete script-writable storage after
  seven days without interaction in affected browser contexts, including
  IndexedDB, service-worker registrations, and cache; first-party Home Screen
  web apps are exempt from that cap. Installed-PWA guidance reduces but does
  not eliminate the need for recovery and storage-loss handling.
  ([WebKit tracking-prevention policy](https://webkit.org/tracking-prevention/), accessed 2026-10-02)
- A service worker can cache the app shell, but correctness cannot depend on
  Background Sync. The API remains "Limited availability," so pending operations
  must retry on startup, focus, successful authentication, and actual network
  responses; background sync can only be an enhancement.
  ([MDN service-worker caching guide](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps/Guides/Caching),
  [Background Synchronization API](https://developer.mozilla.org/en-US/docs/Web/API/Background_Synchronization_API),
  accessed 2026-10-02)
- `navigator.onLine` is only a connectivity hint and can be wrong. CartSync's
  `Offline`, `Saving`, `Synced`, and `Needs attention` states must come from
  local durability, acknowledgements, request outcomes, and sync checkpoints.
  ([MDN `Navigator.onLine`](https://developer.mozilla.org/en-US/docs/Web/API/Navigator/onLine), accessed 2026-10-02)
- Same-origin tabs need explicit coordination. BroadcastChannel is broadly
  available for invalidation or leader election, but the implementation must
  prove that only one uploader claims an operation and that another tab takes
  over after a crash. ([MDN Broadcast Channel API](https://developer.mozilla.org/en-US/docs/Web/API/Broadcast_Channel_API), accessed 2026-10-02)

No candidate can revoke bytes from a disconnected device. Household removal
can stop future server access and reject later uploads, while cached-data purge
is best effort on the next authenticated connection. The security decision must
set the promised behavior explicitly.

### Encryption and key-management implications

- The custom baseline must require TLS to the application server and encrypted
  server storage. Postgres supports TLS plus column, file-system, or block-level
  encryption, but these are deployment choices rather than defaults CartSync
  receives merely by selecting Postgres. ([Postgres encryption options](https://www.postgresql.org/docs/18/encryption-options.html), accessed 2026-10-02)
- IndexedDB does not itself define application-managed encryption.
  Encrypting the local operation log with Web Crypto would require a key
  lifecycle that still works offline; retaining that key on the same device
  limits what household revocation can accomplish. This question belongs in the
  security decision and a local encryption claim must include its key storage
  and recovery model. ([IndexedDB 3.0 specification](https://www.w3.org/TR/IndexedDB/), accessed 2026-10-02)
- PowerSync always uses TLS in transit and its JavaScript Web database supports
  optional at-rest encryption using a ChaCha20-capable SQLite build. CartSync
  would still own key storage and rotation. ([PowerSync data encryption](https://docs.powersync.com/client-sdks/advanced/data-encryption), accessed 2026-10-02)
- RxDB can encrypt selected fields on all supported storage engines. Its free
  CryptoJS wrapper and premium Web Crypto wrapper both require the application
  to supply and manage the password; encrypted fields cannot be queried
  directly. ([RxDB encryption](https://rxdb.info/encryption.html), accessed 2026-10-02)
- Firestore encrypts data in transit and at rest in Google's service, but its
  persistent browser cache still remains on the member's device and is not
  automatically cleared. ([Firebase privacy and security](https://firebase.google.com/support/privacy),
  [Firestore offline persistence](https://firebase.google.com/docs/firestore/manage-data/enable-offline),
  accessed 2026-10-02)
- For the remaining candidates, the reviewed official capabilities do not alter
  the core design obligation: CartSync must specify transport protection,
  server storage protection, local-cache protection, and key management. No
  encrypted offline cache becomes remotely revocable merely because transport
  or server storage is encrypted; the architecture decision must not conflate
  those controls with access withdrawal.

## Comparison matrix

This matrix condenses the cited evidence and analysis in each candidate section
below; it is not a separate source of unsupported product claims.

| Candidate | Durable browser writes | Native convergence fit | Server validation and personal issues | Web/Safari and multi-tab | Backend and operations | License and current MVP cost | Research disposition |
|---|---|---|---|---|---|---|---|
| Custom IndexedDB operation log + Postgres | Direct control; must implement persistence, migration, compaction, and retry | Exact rules are programmable | Exact server authority and Sync Issue projection are programmable | Standards-based, but all Safari/storage/multi-tab handling is owned | Highest engineering and observability burden | Browser APIs and Postgres have no license fee; hosting remains | **Rank 1 baseline** |
| PowerSync | Local SQLite plus persistent FIFO upload queue | Causal checkpoints and field PATCH help; all CartSync-specific rules remain backend logic | Backend owns writes; rejected-intent flow needs deliberate queue handling | Web supported; Safari storage and fallback multi-tab paths need a PoC | Managed or self-hosted sync service plus app backend | Free cloud tier; Pro from $49/month; service source-available, SDK Apache-2.0 | **Rank 2 accelerator** |
| RxDB replication | IndexedDB/Dexie or premium storage; replication retries | Native unit is document state; custom handler or app operation documents required | Custom backend can validate; personal issues remain custom | Built-in BroadcastChannel/leader election; storage choice matters on Safari | CartSync builds and operates replication endpoints | Apache-2.0 core; optimized IndexedDB/OPFS starts at $99/month annually | **Rank 3 accelerator** |
| ElectricSQL | Depends on the app-owned local write store | Read path only; no built-in write-path convergence | Entire outbox, processor, and failure model remain custom | HTTP read sync is web-friendly; local durability remains another component | Electric plus app API and local store | Apache-2.0; small Cloud bills under $5 waived, usage priced | Exclude as complete platform; retain as optional read-path component |
| Firestore | Persistent web cache is opt-in; queued writes survive sessions | Same-document conflicts are last-write-wins; offline transactions fail | Rules validate requests, but operation-level rejection/recovery needs a custom layer | Chrome/Safari/Firefox and multi-tab supported; uncached queries can look empty | Fully managed, but custom processor would reduce simplicity | Usage-priced with a useful free quota | Reject for MVP semantics |
| CouchDB + PouchDB | PouchDB uses IndexedDB and retrying replication | Deterministic document-revision winner retains conflicts, not field/operation convergence | Per-document validation exists; personal issue workflow remains custom | Browser-capable; mature replication, but PouchDB release/incubation risk | Operate CouchDB, tenancy, compaction, backups, conflict cleanup | Apache-2.0; infrastructure only | Reject for semantic and tenancy mismatch |
| Automerge | IndexedDB adapter persists CRDT changes | Strong deterministic convergence; delete-vs-update behavior conflicts with removal-wins | Production sync, authorization, validation, and Sync Issues remain custom | IndexedDB is multi-repo safe; BroadcastChannel needed for live tabs | Operate a production sync server and domain projection | MIT; infrastructure only | Reject as primary platform; useful focused building block |
| Yjs | `y-indexeddb` persists updates | Excellent shared-type CRDT convergence; domain lifecycle rules remain custom | Official threat model assumes trusted writers; binary updates are a poor server-validation boundary | IndexedDB and provider ecosystem support browsers/tabs | Assemble and operate providers, persistence, auth, and projections | MIT; infrastructure/provider costs | Reject as primary platform |

## Candidate analysis

### 1. Custom IndexedDB operation log and server-authoritative Postgres

#### What it provides

This design atomically stores a local materialized view and an immutable pending
operation in IndexedDB. An application-owned uploader sends idempotent batches
to a server operation processor. The processor authenticates the member,
authorizes the household, validates domain invariants, applies or rejects each
operation in Postgres, and returns acknowledgements, canonical changes, and
member-specific Sync Issues.

Postgres supplies unique constraints for normalized identity coalescing and
serializable transactions or explicit locks for first-wins lifecycle changes;
applications must retry serialization failures. These primitives do not define
CartSync semantics, but they provide an authoritative place to implement them.
([Postgres constraints](https://www.postgresql.org/docs/18/ddl-constraints.html),
[transaction isolation](https://www.postgresql.org/docs/18/transaction-iso.html),
accessed 2026-10-02)

Postgres row-level security can restrict rows by household, and enabling RLS
without a policy defaults to denial. Application authorization is still needed
for lifecycle rules and personal Sync Issues.
([Postgres row security](https://www.postgresql.org/docs/18/ddl-rowsecurity.html), accessed 2026-10-02)

#### Fit to CartSync

This is the only candidate whose natural unit can be CartSync's own operation.
The operation processor can explicitly encode causal parents, a stable
operation ID tie-break, remove-wins tombstones, aliases from duplicate identity
coalescing, sequence-move intent, and a compare-and-set completion transition.
The same processor can preserve rejected intent in a member-scoped Sync Issue
while continuing later independent operations.

Durability, unavailable-data markers, and status are also explicit: local data
is available only after a recorded scope checkpoint; `Synced` means every
eligible local operation is acknowledged and the relevant pull cursor is
current, not merely that the device appears online.

#### Cost and risk

The risk is implementation scope. CartSync owns IndexedDB migrations, atomic
view/outbox writes, upload leases and idempotency, pull cursors, retry policy,
compaction, server projections, multi-tab election, telemetry, and recovery
tests. A small MVP can constrain this surface to one domain context and one
operation protocol, but it must not improvise whole-record replacement.

Postgres uses a permissive license with no software fee; browser standards also
have no license fee. Database hosting, backups, logs, and application compute
remain operational costs. ([Postgres license](https://www.postgresql.org/about/licence/), accessed 2026-10-02)

### 2. PowerSync

#### What it provides

PowerSync keeps client-side SQLite synchronized with a backend database. Local
mutations are applied immediately and stored in an ordered upload queue; the
application backend controls how queued `PUT`, `PATCH`, and `DELETE` entries are
validated and applied. Operations must be idempotent and carry a per-client
incrementing ID. ([PowerSync conflict handling](https://docs.powersync.com/handling-writes/handling-update-conflicts), accessed 2026-10-02)

PowerSync documents causal-plus checkpoint behavior: the client keeps its own
pending mutations over the last server checkpoint and does not advance past
unacknowledged writes. This is close to CartSync's automatic retry and local
responsiveness requirements, but the FIFO queue means a permanently failing
entry must be transformed, preserved elsewhere, or explicitly completed so it
does not block unrelated work. ([PowerSync consistency](https://docs.powersync.com/architecture/consistency), accessed 2026-10-02)

The backend is intentionally responsible for custom conflict resolution,
including field-level rules, business validation, server-side conflict records,
and change-status tracking. Those mechanisms make CartSync's semantics
possible, but not automatic. ([PowerSync custom conflict resolution](https://docs.powersync.com/handling-writes/custom-conflict-resolution), accessed 2026-10-02)

#### Fit to CartSync

A promising design would store application operations as first-class local
rows and let the server operation processor create canonical projections. That
retains CartSync's causal metadata and personal Sync Issues instead of treating
PowerSync's default CRUD arrival order as the domain rule. PowerSync then earns
its place by managing local SQLite, ordered retry, download subsets, live
updates, and checkpoints.

Household isolation can be expressed through authenticated sync streams and a
separate authorized application backend. Clients authenticate to PowerSync
with signed JWTs, and the SDK refreshes credentials through the app-provided
connector. ([PowerSync authentication](https://docs.powersync.com/configuration/auth/overview), accessed 2026-10-02)

#### Browser, maintenance, and cost

The JavaScript Web SDK supports browser SQLite storage, but its storage and
multi-tab modes differ by browser. Its broad-compatibility default uses
IndexedDB; Safari/iOS require particular OPFS or BroadcastChannel fallback
paths, and the documentation identifies Safari private-mode limitations. This
must be tested on the oldest supported iPhone/iPad before selection.
([PowerSync JavaScript Web SDK](https://docs.powersync.com/client-sdks/reference/javascript-web), accessed 2026-10-02)

The project is active: the official JavaScript SDK repository published current
Web releases in 2026. ([PowerSync JS releases](https://github.com/powersync-ja/powersync-js/releases), accessed 2026-10-02)

Client SDKs are Apache-2.0, while the self-hosted service's core is under the
Functional Source License and managed/premium editions use commercial terms.
The Cloud Free plan includes 2 GB synchronized per month, 500 MB hosted, and 50
peak clients but deactivates after one inactive week; Pro starts at $49/month.
([PowerSync licensing](https://powersync.com/legal/licensing-terms),
[pricing](https://powersync.com/pricing), accessed 2026-10-02)

### 3. RxDB replication

#### What it provides

RxDB is a local reactive document database with backend-neutral pull/push
replication, checkpoints, retries, and multi-tab leader election. Its server
contract compares the client's assumed master state with the actual master;
conflicts are returned to the client, where a collection conflict handler
chooses or constructs the next document. The default handler discards the fork
in favor of master state. ([RxDB replication](https://rxdb.info/replication.html), accessed 2026-10-02)

#### Fit to CartSync

RxDB removes substantial local-database and replication plumbing, and a custom
backend can authenticate households and enforce server rules. Its native unit,
however, is a document state. CartSync would still need operation documents or
a custom field-operation layer to prevent independent edits being folded into
whole-document conflicts and to preserve removal-losing intent.

A client-side conflict handler is not sufficient authority for hostile or
revoked clients. Canonical acceptance, duplicate coalescing, first completion,
Department ordering, and Sync Issue creation still belong in the server
processor. Device timestamps are also unsuitable as the causal tie-break, so
CartSync would supply its own operation metadata.

#### Browser, maintenance, and cost

RxDB coordinates tabs with BroadcastChannel and leader election so one instance
runs replication. The free Dexie storage uses IndexedDB and is positioned for
prototypes; the optimized plain IndexedDB and OPFS storages are premium.
([RxDB IndexedDB sync](https://rxdb.info/articles/indexeddb/indexeddb-sync.html),
[Dexie storage](https://rxdb.info/rx-storage-dexie.html), accessed 2026-10-02)

RxDB's Apache-2.0 core includes replication and up to 13 open collections. Pro
starts at $99/month billed annually and adds optimized IndexedDB/OPFS, SQLite,
and WebCrypto encryption; Pro Plus starts at $239/month. The repository shipped
v17 releases in 2026 and is actively maintained. ([RxDB pricing](https://rxdb.info/premium/),
[releases](https://github.com/pubkey/rxdb/releases), accessed 2026-10-02)

### 4. ElectricSQL

Current Electric Sync is a **read-path** sync engine that streams Postgres
Shapes to clients over HTTP. Its write guide explicitly says it does not provide
or prescribe built-in write-path synchronization; durable offline writes
require application-owned persistent optimistic state, an API, or another
framework. Legacy bidirectional ElectricSQL documentation must not be mistaken
for current product behavior. ([Electric overview](https://electric-sql.com/docs/intro),
[writes guide](https://electric-sql.com/docs/guides/writes), accessed 2026-10-02)

Electric can therefore accelerate live authorized reads from Postgres but does
not reduce the hardest CartSync work: durable operations, upload retry, causal
metadata, validation failures, Sync Issues, and canonical convergence. Adding
it to the custom baseline creates another deployed component and a second local
state mechanism unless its read fan-out is demonstrably valuable.

Electric is Apache-2.0 and actively developed. Cloud PAYG charges $1 per million
stream writes and $0.10 per GB-month retention, waives monthly bills below $5,
and adds $2 per million emitted writes for Postgres Sync; Pro is $249/month.
([Electric license](https://github.com/electric-sql/electric/blob/main/LICENSE),
[pricing](https://electric.ax/pricing), accessed 2026-10-02)

**Disposition:** exclude as a complete synchronization architecture; retain as
an optional read-path component only if later scale evidence justifies it.

### 5. Firebase Cloud Firestore

Firestore's web SDK can cache actively used data, accept offline writes, resume
sync, and coordinate persistent IndexedDB across tabs. On web, persistence is
opt-in and officially supported in Chrome, Safari, and Firefox. Multiple changes
to the same document use last-write-wins. A query with no cached documents can
return an empty result, although snapshot metadata exposes `fromCache`; CartSync
would have to wrap this to distinguish unavailable data from a true empty set.
([Firestore offline persistence](https://firebase.google.com/docs/firestore/manage-data/enable-offline), accessed 2026-10-02)

Firestore transactions retry concurrent online edits but fail while offline.
That prevents client transactions from directly implementing offline
first-completion or duplicate-coalescing rules. ([Firestore transactions](https://firebase.google.com/docs/firestore/manage-data/transactions), accessed 2026-10-02)

Firebase Authentication and Security Rules can enforce household access and
validate proposed document state. They do not expose an application operation
queue or automatically preserve rejected local intent; adding an operations
collection plus a trusted server processor would recreate much of the custom
baseline while retaining Firestore's data-model and provider lock-in.
([Firestore security overview](https://firebase.google.com/docs/firestore/security/overview),
[rules conditions](https://firebase.google.com/docs/firestore/security/rules-conditions), accessed 2026-10-02)

Firestore is managed and usage-priced. The current free quota for one database
per project includes 1 GiB storage, 50,000 document reads/day, 20,000 writes/day,
20,000 deletes/day, and 10 GiB outbound/month; listeners can incur fresh-query
reads after long offline disconnects. ([Firestore pricing](https://firebase.google.com/docs/firestore/pricing), accessed 2026-10-02)

The official Firebase JavaScript release notes contain current 2026 Firestore
SDK releases, so the web client is actively supported.
([Firebase JavaScript release notes](https://firebase.google.com/support/release-notes/js), accessed 2026-10-02)

**Disposition:** reject for the MVP because document LWW, offline transaction
failure, cache ambiguity, and the custom operation layer needed to repair them
erase its main simplicity advantage.

### 6. CouchDB and PouchDB

PouchDB provides an IndexedDB-backed browser database and implements CouchDB's
bidirectional replication. Live replication can retry after lost connectivity.
CouchDB/PouchDB preserve conflicting document revision leaves and select one
deterministic but arbitrary visible winner; applications must fetch, merge, and
delete losing leaves. ([PouchDB replication](https://pouchdb.com/guides/replication.html),
[conflicts](https://pouchdb.com/guides/conflicts.html), accessed 2026-10-02)

That is reliable document replication, but it is not CartSync's field- and
operation-level model. An arbitrary visible winner can hide intended state from
normal views until a custom conflict sweeper resolves it. Removal recovery,
identity aliases, ordered moves, first completion, and personal Sync Issues
would all require an application merge layer.

CouchDB can validate new versus old document revisions and user context, but
authorization is principally database membership. Strong household isolation
would likely require database-per-household or an application proxy/validation
scheme, adding operational and query complexity compared with row-scoped
Postgres tenancy. ([CouchDB validation](https://docs.couchdb.org/en/stable/ddocs/ddocs.html#validate-document-update-functions),
[security](https://docs.couchdb.org/en/stable/intro/security.html), accessed 2026-10-02)

Both are Apache-2.0 and self-hostable. CouchDB has current 3.5 documentation,
while PouchDB's latest formal release remains 9.0.0 and the project is undergoing
Apache incubation; its 2026 incubator report describes active development but
unfinished release and infrastructure work. ([PouchDB releases](https://github.com/apache/pouchdb/releases),
[Apache Incubator report](https://cwiki.apache.org/confluence/spaces/INCUBATOR/pages/430408141/June2026),
accessed 2026-10-02)

**Disposition:** reject because revision-tree conflict cleanup, tenancy, and
current PouchDB release risk outweigh the replication it supplies.

### 7. Automerge as a CRDT building block

Automerge offers a JSON-like CRDT, compact change history, and transport-neutral
sync. Its Repo IndexedDB adapter persists across restarts and is safe for
concurrent repositories; storage alone does not live-update other tabs, so a
BroadcastChannel network adapter is also needed. Already loaded documents remain
editable offline and converge after reconnect. The public Automerge sync server
is explicitly experimental, so production software must operate its own.
([Automerge storage](https://automerge.org/docs/reference/repositories/storage/),
[network sync](https://automerge.org/docs/tutorial/network-sync/), accessed 2026-10-02)

Independent properties merge. Concurrent scalar writes choose the same winner
using an operation counter and actor ID rather than wall-clock time, while losing
values remain inspectable. That aligns with deterministic ties and recoverable
intent. ([Automerge conflicts](https://automerge.org/docs/reference/documents/conflicts/), accessed 2026-10-02)

Its native delete/update semantics do not match CartSync: a concurrent map set
survives a delete, whereas CartSync requires removal to win and preserve the set
as a personal issue. Server authorization, first completion, identity
coalescing, and domain validation also remain custom.
([Automerge merge rules](https://automerge.org/docs/reference/under-the-hood/merge-rules/), accessed 2026-10-02)

Automerge is MIT-licensed and its official repositories published current 2026
releases. ([Automerge license](https://github.com/automerge/automerge/blob/main/LICENSE),
[releases](https://github.com/automerge/automerge/releases), accessed 2026-10-02)

**Disposition:** reject as the primary platform. Consider only if a focused PoC
shows its operation metadata/history materially simplifies a custom reducer
without weakening server authority.

### 8. Yjs as a CRDT building block

Yjs document updates are commutative, associative, and idempotent, and state
vectors identify missing updates. The `y-indexeddb` provider persists a document
locally and enables offline editing, while network providers are separately
assembled. ([Yjs document updates](https://docs.yjs.dev/api/document-updates),
[IndexedDB provider](https://docs.yjs.dev/ecosystem/database-provider/y-indexeddb), accessed 2026-10-02)

Yjs is optimized for trusted collaborative writers. Its official threat model
states that a peer with write access can corrupt a document and that filtering
binary updates server-side is not a sound security boundary. That conflicts with
CartSync's need to validate every offline action from potentially revoked or
outdated clients before it enters canonical household state.
([Yjs threat model](https://github.com/yjs/yjs/blob/main/THREAT_MODEL.md), accessed 2026-10-02)

Yjs is MIT-licensed, actively maintained, and had stable and release-candidate
updates in 2026. ([Yjs license](https://github.com/yjs/yjs/blob/main/LICENSE),
[releases](https://github.com/yjs/yjs/releases), accessed 2026-10-02)

**Disposition:** reject as the primary platform. It is an excellent shared-text
CRDT, but CartSync would still need a parallel trusted operation system for all
of its important domain semantics.

## Explicit semantic coverage

The table below distinguishes a useful primitive from a complete CartSync rule.
"Custom" means the candidate can host the rule but does not provide it.

| Confirmed rule | Custom op log | PowerSync | RxDB | Electric | Firestore | Couch/Pouch | Automerge | Yjs |
|---|---|---|---|---|---|---|---|---|
| Durable offline add/check and restart recovery | Custom/native IDB | Built in | Built in | Custom write store | Built in when enabled | Built in | Built in with adapter | Built in with provider |
| Independent-field merge | Custom reducer | Backend custom; PATCH is useful | Custom handler/ops | Custom | Only if fields/documents are modeled carefully | Custom revision merge | Native for separate properties | Native shared types |
| Same-field causal order and deterministic non-clock tie | Custom metadata | Custom metadata/backend | Custom metadata/handler | Custom | Native LWW is not CartSync's specified rule | Arbitrary revision winner | Native deterministic tie | Native CRDT order, domain rule custom |
| Concurrent check/uncheck | Custom reducer | Backend custom | Custom | Custom | LWW only | Custom | Custom data modeling | Custom data modeling |
| Remove wins and losing edit becomes personal issue | Custom reducer/projection | Backend and issue model custom | Backend custom | Custom | Custom operation processor | Custom conflict cleanup | Native rule conflicts | Custom operation layer |
| Concurrent duplicate creation and Trip addition coalesce | Unique identity + aliases | Backend custom | Backend custom | Custom | Online server processor required | Custom cleanup | Custom domain projection | Custom domain projection |
| Department moves preserve unaffected order | Custom sequence operations | Backend custom | Custom | Custom | Custom | Custom | Sequence CRDT helps, exact move rule custom | Sequence CRDT helps, exact move rule custom |
| First completion wins; late edits become corrections/issues | Transactional lifecycle reducer | Backend custom | Backend custom | Custom | Offline transaction unavailable | Custom revision logic | Custom lifecycle projection | Custom lifecycle projection |
| Archived Product allowed; archived Store rejects new Trip | Server validation + issue | Backend custom | Backend custom | Custom | Rules/functions plus custom issue | Validation plus custom issue | Custom projection | Custom operation layer |
| Transient retry vs permanent attention without global blockage | Custom classification and per-op ack | Queue built in; permanent failure needs explicit draining design | Retry built in; issue classification custom | Custom | SDK retry plus custom rejection capture | Retry/denied events plus custom issue | Network custom | Provider custom |
| Unsynchronized data is unavailable, never empty | Explicit scope checkpoint | Sync/checkpoint state can support it | Explicit replication checkpoint | Shape state plus custom local state | Must override empty-cache behavior | Explicit replication readiness | Document availability custom | Provider readiness custom |
| Household isolation and revocation | API auth + RLS | JWT sync scopes + backend auth | Custom endpoints | Auth proxy + custom writes | Auth + Security Rules | Database membership/proxy | Custom server | Poor fit for untrusted writers |

## Ranked recommendation for the architecture decision

### 1. Use the custom operation-log/Postgres design as the reference baseline

Every other candidate should be measured against this baseline's semantic
clarity, testability, and ownership cost. It is the recommended default if two
short PoCs show that IndexedDB/multi-tab reliability and reducer complexity are
manageable for the MVP team.

This is not a recommendation to build a general-purpose sync engine. The scope
should remain one domain-specific operation protocol, one household partition,
server-generated canonical sequence, and a small set of projections.

### 2. Evaluate PowerSync as the only managed accelerator in the final choice

PowerSync most directly removes transport and local-SQLite work while preserving
a server-authoritative backend. It should win only if the Web/Safari PoC passes,
permanent failures can leave the FIFO queue without losing intent, and the
application operation envelope remains first-class rather than being flattened
into arrival-ordered CRUD.

### 3. Keep RxDB as the library-led fallback

RxDB is attractive if the team values a JavaScript-native reactive database and
custom endpoints over a managed service. It should win only if its document
replication can carry operation documents cleanly, free Dexie storage passes the
Safari durability/performance tests, and the premium-license tradeoff is
acceptable if it does not.

### 4. Do not advance the remaining candidates to architecture selection

- Electric duplicates only the read side while leaving all decisive write work.
- Firestore's document LWW and offline transaction limits require a parallel
  operation system and preserve significant provider lock-in.
- CouchDB/PouchDB impose revision-conflict and tenancy models that do not align
  with the household domain.
- Automerge and Yjs are CRDT ingredients, not authorized application sync
  platforms; their strengths do not eliminate CartSync's trusted reducer.

## Unresolved risks

1. **Safari durability and storage loss.** No browser library removes quota,
   eviction, private-mode, or user-cleared-data risk. The product needs explicit
   loss messaging and a tested persistence request flow.
2. **Cross-tab ownership.** Duplicate upload, abandoned leases, and database
   upgrade blocking must be tested, especially on iOS where tab/process
   suspension is aggressive.
3. **Operation compaction.** CartSync needs a policy for how long client and
   server operation history remains necessary for causal comparison, replay,
   debugging, and Sync Issue restoration.
4. **Revocation.** Removing a member cannot erase an offline cache remotely.
   The architecture can reject future operations and purge on reconnect; the
   security decision must define the promised limits.
5. **Causal metadata size.** Full vector clocks may be unnecessary for a small
   household, but a per-device sequence plus observed server watermark must be
   proven sufficient for same-field order, tie detection, and compaction.
6. **Ordered-list convergence.** Department moves need a domain-specific
   sequence algorithm with invariants and property tests; ordinary integer
   positions or generic row LWW are insufficient.
7. **Migration recovery.** Client schema and operation-version changes must be
   forward-compatible with long-offline devices or safely become Sync Issues.
8. **Observability.** Queue age, retry class, rejected-operation reasons,
   checkpoint lag, and reducer-version mismatches must be diagnosable without
   exposing household shopping data.

## Focused proof-of-concept questions

The architecture decision should request evidence from two bounded spikes, not
production code.

### Spike A: custom IndexedDB baseline

1. Can one strict IndexedDB transaction atomically persist a local state change
   and its operation, survive an immediate tab/process kill on current Chrome,
   Firefox, desktop Safari, and mobile Safari, and replay exactly once?
2. Can two tabs elect one uploader, recover after leader termination, and avoid
   duplicate visible effects even when the server receives duplicate batches?
3. Can a small reference reducer deterministically pass these paired histories:
   check/uncheck, same-field edits, remove/edit recovery, duplicate Product and
   Trip Entry creation, overlapping Department moves, two completions, late
   completion edits, archived Product use, and archived Store rejection?
4. Can the client distinguish never-synchronized scope, synchronized empty
   scope, pending local changes, acknowledged state, transient retry, and a
   permanent personal Sync Issue after a restart?

### Spike B: PowerSync Web accelerator

1. Do the same kill/restart and two-tab tests pass with the documented Safari
   storage mode on the oldest supported iPhone and iPad, including private-mode
   failure handling?
2. Can CartSync upload first-class operation rows with causal metadata and
   receive canonical projections without PowerSync's default CRUD/LWW becoming
   the domain rule?
3. When one upload is permanently unauthorized or invalid, can it be copied to
   a personal Sync Issue and acknowledged so unrelated later operations sync,
   without losing the original member intent?
4. Can household removal stop new sync, reject stale offline operations, and
   trigger a best-effort local purge without exposing another household's rows?
5. What measured bundle, startup, memory, storage, and monthly synchronized-data
   costs result from representative household catalogs and retained Trip history?

If Spike B fails Safari or queue-isolation requirements, run the same operation
document and reducer tests through RxDB before committing to the fully custom
transport. If both accelerators still require equivalent custom machinery, the
custom baseline is the simpler long-term system.
