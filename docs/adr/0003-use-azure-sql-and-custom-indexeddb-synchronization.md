---
status: accepted
---

# Use Azure SQL and Custom IndexedDB Synchronization

CartSync will use an Angular 22 local-first PWA, a .NET 10 ASP.NET Core modular
monolith, and Azure SQL Database as the sole authoritative server database.
The browser will persist canonical projections and typed pending operations in
native IndexedDB, while the server serializes operations per Household and
applies the convergence rules from ADR-0002. This preserves CartSync's exact
offline semantics without adding a second synchronization database or adapting
the domain to a document-replication model.

## Considered Options

- RxDB with free Dexie storage passed the same spike gates, but doubled the
  initial browser transfer and required pending operations to be embedded in
  projection documents to retain one atomic local intent boundary.
- PowerSync was rejected because self-hosting with Azure SQL would also require
  a PostgreSQL or MongoDB bucket store, violating the single-server-database
  constraint.
- Generic CRUD synchronization and whole-record replacement were rejected
  because they cannot implement ADR-0002's field- and operation-level rules.
- Full event sourcing was rejected because CartSync needs bounded operation
  receipts and current canonical projections, not a permanent event history.

## Consequences

- CartSync owns IndexedDB migrations, the outbox, uploader leadership, rebasing,
  checkpoints, retries, and storage-loss recovery.
- Azure SQL stores canonical relational state, convergence metadata, personal
  Sync Issues, and 180 days of metadata-only operation receipts.
- Checkpoint pulls are authoritative; SSE only announces that newer changes may
  be available.
- The custom synchronization code must continue passing the shared reducer,
  browser, multi-tab, migration, and Azure integration histories before release.
