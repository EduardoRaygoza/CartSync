# Azure SQL Synchronization Spike Comparison

Evidence date: 2026-10-02

## Recommendation status

The custom native IndexedDB boundary is the provisional winner. It passes every automated comparison gate that RxDB/Dexie passes, keeps the operation envelope and outbox visible, produces a 52% smaller initial JavaScript transfer, and adds no client database abstraction. This matches the decision rule: prefer custom synchronization when it passes.

The architecture decision is **not yet final**. WebKit/iOS hardware, an actual Azure deployment, service-worker migration, quota/storage-loss recovery, and several complete ADR-0002 histories remain open evidence gates. Issue #11 must stay open until those limitations are reviewed and the custom approach is explicitly accepted.

Disposable branches:

- [Custom native IndexedDB](https://github.com/EduardoRaygoza/CartSync/tree/codex/spike-custom-azure-sql-sync)
- [RxDB 17.5.0 with Dexie 4.4.6](https://github.com/EduardoRaygoza/CartSync/tree/codex/spike-rxdb-azure-sql-sync)

## Controlled comparison

Both branches use the same:

- .NET 10 ASP.NET Core controller API;
- typed operation envelope and per-operation outcomes;
- canonical reducer and ADR-0002 fixtures;
- Azure SQL-compatible relational schema;
- `Microsoft.Data.SqlClient` transaction gate;
- per-Household transaction-owned `sys.sp_getapplock`;
- `SESSION_CONTEXT` set and explicit cleanup before connection-pool return;
- row-level security predicate, public UUID/internal `bigint IDENTITY` split, normalized-name binary unique key, field versions, aliases, tombstones, Sync Issues, and 180-day receipt metadata;
- Angular 22 UI, sample data, test selectors, and browser scenarios.

Only the browser persistence boundary differs. The custom branch writes the optimistic projection and operation into separate native IndexedDB stores in one strict-durability transaction, then coordinates tabs with an IndexedDB lease and BroadcastChannel. The RxDB branch uses free Dexie storage, RxDB multi-instance leadership, and one entry document containing both the projection and serialized pending operations so each user intent is atomic without premium storage.

## Results

| Gate                                   | Custom IndexedDB |                 RxDB/Dexie |
| -------------------------------------- | ---------------: | -------------------------: |
| .NET reducer/schema tests              |        23 passed |                  23 passed |
| SQL transaction/pool tests             |         2 passed | Same shared implementation |
| Chromium browser tests                 |         5 passed |                   5 passed |
| Firefox browser tests                  |         5 passed |                   5 passed |
| Chromium interaction p95               |         64.93 ms |                   65.32 ms |
| Chromium durable commit p95            |          14.3 ms |                    15.6 ms |
| Firefox interaction p95                |         67.31 ms |                   66.46 ms |
| Firefox durable commit p95             |           2.0 ms |                     5.0 ms |
| Initial JavaScript, raw                |        215.56 kB |                  450.87 kB |
| Initial JavaScript, estimated transfer |         59.75 kB |                  127.27 kB |
| Additional runtime dependencies        |             None |             RxDB and Dexie |
| Paid browser storage                   |             None |  Excluded; free Dexie used |

Timing is a 20-operation local run on this development host, not a production service-level measurement. It proves the 100 ms rendering and 250 ms durable-commit gates with substantial margin in the tested engines. Both branches restored durable pending work after reload well under the three-second gate and converged two tabs under a single uploader owner.

The large-household fixture creates 10 members, 25 Stores, 5,000 Products, and 1,000 retained Trips, then serializes 6,000 canonical changes. It passed the ten-second bootstrap-processing gate in the shared reducer suite. This is an in-process processor/serialization measurement; network transfer and browser hydration of a production paged bootstrap remain to be measured in Azure.

## Semantics exercised

The automated suite proves:

- offline check/uncheck with atomic projection/outbox durability and restart recovery;
- operation replay with exactly one visible effect;
- independent-field merge;
- deterministic concurrent same-field and check/uncheck ties without device clocks;
- remove-versus-unseen-edit preservation as a personal Sync Issue;
- normalized Product duplicate coalescing and alias use when adding a Trip Entry;
- one Product per Trip through canonical Trip Entry coalescing;
- first-wins Trip completion and a Sync Issue for the competing completion;
- rejection of a new Trip for an archived Store;
- invalid units/amounts and unsupported operations as per-operation `needs attention` outcomes;
- per-Household serialization, monotonic cursors, transaction rollback, duplicate upload handling, and session-context cleanup through a reused connection pool;
- NFKC, whitespace, invariant case-fold, accent, and punctuation normalization conformance between JavaScript and .NET fixtures;
- cross-tab propagation, uploader ownership, relaunch, and online-only fallback state.

The following confirmed semantics are represented in fixtures or schema but still need end-to-end histories before final acceptance: overlapping relative Department moves, archived-Product additions without restoration, Completed Trip corrections, competing carry-forward choices, unavailable scopes, offline Sync Issue resolution, authorization loss, quota exhaustion, destructive storage loss, and protocol/schema migration.

## Engineering findings

### Custom

The custom boundary remained small and direct. IndexedDB can atomically commit separate projection and outbox records, so the data layout does not have to bend around a library transaction model. Lease expiry and BroadcastChannel invalidation are explicit and inspectable. The principal risk is ownership: CartSync must implement migrations, rebasing, pull checkpoints, retry classification, compaction, and observability itself.

### RxDB/Dexie

RxDB removed reactive-query and multi-tab leadership plumbing. It also introduced two spike findings:

1. `ignoreDuplicate` is development-only and fails in a production build, so real multi-instance behavior must be used and tested.
2. A projection collection plus a separate operation collection would not give CartSync the desired single local user-intent boundary. The spike therefore embeds typed pending-operation JSON in the projection document. That passes atomic durability, but couples outbox retention and compaction to projection documents and makes the synchronization model less transparent.

The RxDB initial transfer was about 67.5 kB larger after compression. Its measured interaction and commit timings were otherwise equivalent at this scale. Because custom passed the same gates, RxDB does not meet the plan's fallback-only selection condition.

## Azure SQL and deployment evidence

The SQL gates ran against a disposable SQL Server 2022 container using Azure SQL-compatible primitives. Twelve concurrent processors received a gap-free per-Household cursor, a failed transaction did not advance it, and pooled connections exposed neither prior Household nor member context.

The spike deliberately did not create billable Azure resources. Recorded Azure consumption and cost are therefore zero for this evidence run, not a production forecast. An Azure run must still measure SQL vCore seconds, Container Apps vCPU/GiB seconds and requests, storage, scale-to-zero recovery, SSE reconnection, firewall reachability, and the projected monthly total.

The cost hypothesis remains plausible but unproved: Azure SQL's free offer includes 100,000 serverless vCore-seconds plus 32 GB each of data and backup storage per database, and can continue into paid General Purpose serverless usage instead of becoming unavailable. Container Apps includes 180,000 vCPU-seconds, 360,000 GiB-seconds, and two million requests monthly before usage charges; Static Web Apps has a no-SLA Free plan. These allowances support a $25/month MVP target, but only an instrumented Canada Central deployment can validate it. ([Azure SQL free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql), [Container Apps pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/), [Static Web Apps pricing](https://azure.microsoft.com/en-us/pricing/details/app-service/static/), accessed 2026-10-02)

The proposed restricted public SQL endpoint is consistent with Azure's network controls: new logical servers deny connections by default, while the broad `0.0.0.0` “Allow Azure services” rule permits resources outside the subscription and should remain disabled. Container Apps can use a managed identity to obtain an Azure SQL token, but database roles still need explicit configuration. ([Azure SQL network controls](https://learn.microsoft.com/en-us/azure/azure-sql/database/network-access-controls-overview?view=azuresql), [Container Apps managed identity](https://learn.microsoft.com/en-us/azure/container-apps/managed-identity), accessed 2026-10-02)

No Bicep `what-if`, managed-identity login, firewall probe, GitHub Actions deployment, or Azure cost alert was run because this host has neither Azure CLI nor an authenticated subscription context. GitHub OIDC remains the correct credential-free deployment boundary, subject to a narrowly scoped federated trust condition. ([GitHub OIDC for Azure](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure), accessed 2026-10-02)

## PowerSync exclusion

PowerSync is not spiked. With Azure SQL as the source, its self-hosted sync service would still require a separate PostgreSQL or MongoDB bucket store. That violates the confirmed rule that Azure SQL is the sole authoritative and synchronization-supporting server database; Azure SQL CDC would also add operational surface without removing CartSync's canonical reducer.

## Remaining acceptance gates

Before accepting custom synchronization and publishing the final architecture, OpenAPI contract, Bicep, and ADR:

1. Run the missing ADR-0002 end-to-end histories listed above.
2. Exercise current and previous Chrome, Firefox, and Safari releases. This host passed current Playwright Chromium and Firefox; WebKit could not launch because three host libraries require an administrator-authenticated installation.
3. Test on real iPhone and iPad hardware running iOS/iPadOS 17 or newer, including installed-PWA storage, private browsing, eviction, and storage loss.
4. Deploy the disposable API/schema to Azure SQL and Container Apps in Canada Central; verify auto-resume, transient retries, scale-to-zero, direct SSE, restricted firewall rules, managed identity, RLS, and pool isolation.
5. Add and validate Bicep, GitHub OIDC deployment, OpenAPI generation/client drift, expand-contract migrations, and the 90-day old-handler window.
6. Measure actual Azure consumption and enforce the $25 target with cost and remaining-vCore alerts.

If review accepts these limitations as post-selection implementation gates, choose the custom IndexedDB architecture. If every gate must pass before selection exactly as originally stated, keep issue #11 open and run this list first.
