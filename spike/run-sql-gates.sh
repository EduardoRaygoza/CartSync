#!/usr/bin/env bash
set -euo pipefail

container_name="cartsync-sync-spike-sql"
password="CartSync_Spike!2026"
port="14339"

cleanup() { docker rm -f "$container_name" >/dev/null 2>&1 || true; }
trap cleanup EXIT
cleanup
docker run --name "$container_name" -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$password" -p "$port:1433" -d mcr.microsoft.com/mssql/server:2022-latest >/dev/null

for _ in $(seq 1 60); do
  if docker exec "$container_name" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$password" -Q "SELECT 1" >/dev/null 2>&1; then break; fi
  sleep 1
done

docker exec "$container_name" /opt/mssql-tools18/bin/sqlcmd -b -C -S localhost -U sa -P "$password" -Q "CREATE DATABASE CartSyncSpike"
docker cp server/CartSync.SyncApi/Database/schema.sql "$container_name:/tmp/schema.sql"
docker exec "$container_name" /opt/mssql-tools18/bin/sqlcmd -b -C -S localhost -U sa -P "$password" -d CartSyncSpike -i /tmp/schema.sql

export CARTSYNC_SQL_CONNECTION="Server=127.0.0.1,$port;Database=CartSyncSpike;User ID=sa;Password=$password;TrustServerCertificate=True;Encrypt=True;Max Pool Size=20"
/snap/bin/dotnet test ../CartSync.SyncSpike.slnx --configuration Release --filter AzureSqlIntegrationTests
