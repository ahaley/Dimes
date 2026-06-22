# Deploying Dimes behind a reverse proxy

In production the SPA and the API are served under **one origin** by a reverse proxy. This keeps the
BFF session cookie and the Keycloak OIDC redirect flow same-origin (the requirement that drove the
dev Vite-proxy setup) while leaving the frontend and backend **independent**: built separately,
deployed separately, composed only at the proxy.

```
                       https://dimes.example.com
                                  │
                         ┌────────┴────────┐
                  /api, /hubs,           everything
                  /signin-oidc,            else
                  /health                    │
                         │              static SPA
                    .NET API            (web/dist)
                  127.0.0.1:5080
```

Two interchangeable configs are provided:
- `nginx/dimes.conf`
- `Caddyfile` (automatic TLS)

## Required API setting

Both proxies terminate TLS and forward plain HTTP to the API, so the API must honor the forwarded
scheme/host or it will build `http://` OIDC redirect_uris and mis-set the `Secure` cookie. Enable:

```
Proxy__UseForwardedHeaders=true
```

This is **off by default** and must only be enabled when the API is reachable *exclusively* through
the proxy (bind it to `127.0.0.1` / an internal network). A directly-reachable API with this on
would let clients spoof `X-Forwarded-*`.

## Steps

1. **Build the SPA** and place it where the proxy's `root` points (`/var/www/dimes` in the samples):
   ```bash
   cd web && npm run build      # outputs web/dist
   # copy web/dist/* to /var/www/dimes
   ```
2. **Run the API** on an internal address, e.g.:
   ```bash
   Proxy__UseForwardedHeaders=true \
   Auth__Mode=Oidc Auth__Oidc__Authority=https://<kc-host>/realms/<realm> \
   Auth__Oidc__ClientId=dimes Auth__Oidc__ClientSecretRef=DIMES_OIDC_CLIENT_SECRET \
   DIMES_OIDC_CLIENT_SECRET=<secret> Auth__SiteAdmin__Email=admin@example.com \
   dotnet src/Dimes.Api/bin/Release/net10.0/Dimes.Api.dll --urls http://127.0.0.1:5080
   ```
3. **Point the proxy** at your domain + certs (nginx) — Caddy fetches certs automatically — and
   reload it.
4. **Register the redirect URI** in Keycloak (Clients → dimes → Valid redirect URIs):
   ```
   https://dimes.example.com/signin-oidc
   ```
   and set Web origins to `https://dimes.example.com`.

## Routing note

Only `/api`, `/hubs`, `/signin-oidc`, and `/health` go to the API; everything else is the SPA with
an `index.html` fallback for client-side routes. If you ever move the OIDC callback under `/api`
(via `Auth:Oidc:CallbackPath`), the API routes collapse to just `/api` + `/hubs` and you can drop
the dedicated `/signin-oidc` rule. (`/openapi` is dev-only — `Program.cs` maps it only in
Development — so it is intentionally not proxied here.)
