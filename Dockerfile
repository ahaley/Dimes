# Dimes API image (the React SPA deploys separately as a static site — see .do/app.yaml).
# Multi-stage: build/publish with the .NET 10 SDK, run on the lean ASP.NET runtime.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against just the csproj graph first so layer caching survives source-only changes.
COPY src/Dimes.Domain/Dimes.Domain.csproj src/Dimes.Domain/
COPY src/Dimes.Infrastructure/Dimes.Infrastructure.csproj src/Dimes.Infrastructure/
COPY src/Dimes.Infrastructure.Postgres/Dimes.Infrastructure.Postgres.csproj src/Dimes.Infrastructure.Postgres/
COPY src/Dimes.Api/Dimes.Api.csproj src/Dimes.Api/
RUN dotnet restore src/Dimes.Api/Dimes.Api.csproj

COPY src/ src/
RUN dotnet publish src/Dimes.Api/Dimes.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# App Platform routes to this port; the API binds it via ASPNETCORE_URLS (see .do/app.yaml env).
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Dimes.Api.dll"]
