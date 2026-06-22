# Keycloak client for Dimes

`dimes-client.json` is a Keycloak **client representation** you can import to create the
OIDC client that Dimes uses in `Auth:Mode = Oidc`.

## Why it's shaped this way

Dimes runs the authorization-code flow **on the server** and issues a single httpOnly
session cookie — no tokens ever reach the browser (BFF pattern). So the client is:

- **Confidential** (`publicClient: false`) — Keycloak generates a client secret.
- **Standard flow only** — implicit, direct access grants, and service accounts are off.
- Granted the `email` + `profile` scopes, which supply the `email`, `email_verified`, and
  `name` claims that `AuthExtensions.OnTokenValidatedAsync` reads to JIT-provision an actor.

## Import

1. Keycloak admin console → select (or create) your realm → **Clients** → **Import client**.
2. Choose `dimes-client.json` → **Save**.
3. Before saving, **edit the URLs** for your deployment (the file ships with
   `https://dimes.example.com` placeholders):
   - **Valid redirect URI** → `<your-origin>/signin-oidc`
     (matches `Auth:Oidc:CallbackPath`, default `/signin-oidc`).
   - **Web origin** → `<your-origin>`.
   - **Valid post logout redirect URI** → `<your-origin>/`.

   For local dev that origin is `http://localhost:5080`.

## Wire it into Dimes

After import, open **Clients → dimes → Credentials** and copy the generated secret.
Then configure Dimes (env vars shown; or an uncommitted `appsettings.Production.json`):

```
Auth__Mode=Oidc
Auth__Oidc__Authority=https://<keycloak-host>/realms/<realm>
Auth__Oidc__ClientId=dimes
Auth__Oidc__ClientSecretRef=DIMES_OIDC_CLIENT_SECRET
DIMES_OIDC_CLIENT_SECRET=<the secret from Keycloak>
Auth__SiteAdmin__Email=admin@example.com
```

`ClientSecretRef` is the *name* of the secret, resolved at startup via `ISecretResolver`
(env var or `Secrets:<ref>` config) — the secret value itself never goes in config or the DB.
The bootstrap site admin is matched by `Auth:SiteAdmin:Email` against the email claim on
first login.
