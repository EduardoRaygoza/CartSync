FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/server/CartSync.Api/CartSync.Api.csproj src/server/CartSync.Api/
RUN dotnet restore src/server/CartSync.Api/CartSync.Api.csproj
COPY src/server/CartSync.Api src/server/CartSync.Api
RUN dotnet publish src/server/CartSync.Api/CartSync.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CartSync.Api.dll"]
