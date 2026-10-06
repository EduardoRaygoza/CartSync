FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/server/CartSync.DatabaseMigrator/CartSync.DatabaseMigrator.csproj src/server/CartSync.DatabaseMigrator/
RUN dotnet restore src/server/CartSync.DatabaseMigrator/CartSync.DatabaseMigrator.csproj
COPY src/server/CartSync.DatabaseMigrator src/server/CartSync.DatabaseMigrator
RUN dotnet publish src/server/CartSync.DatabaseMigrator/CartSync.DatabaseMigrator.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0.12
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "CartSync.DatabaseMigrator.dll"]
