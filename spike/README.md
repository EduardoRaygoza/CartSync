# CartSync Azure SQL synchronization spike

This disposable branch tests the custom native IndexedDB synchronization boundary against the same .NET 10 canonical reducer and Azure SQL-compatible schema used by the RxDB comparison branch.

## Run

```bash
cd spike/client && npx --yes --package node@22.22.3 node node_modules/@angular/cli/bin/ng.js build && npx --yes --package node@22.22.3 node node_modules/@playwright/test/cli.js test
```

Run reducer tests from the repository root with `/snap/bin/dotnet test CartSync.SyncSpike.slnx`. Run `spike/run-sql-gates.sh` from the `spike` directory to start the disposable SQL Server container and exercise `sp_getapplock`, rollback, cursor monotonicity, row-level context, and connection-pool cleanup.

The state inspector exposes local projections, pending operations, checkpoint, uploader lease, and local commit timing. Add `?liveApi=1` to send uploads to the ASP.NET API at `http://localhost:5050`; without it the browser harness deterministically acknowledges operations locally so durability and multi-tab tests need no running server.
