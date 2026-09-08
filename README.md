# Ustin.Work.LessMess.UmbarcoWrapper

A thin **auth + translations wrapper** in front of an Umbraco CMS instance.

The wrapper owns no database. Every meaningful operation (register, login, list
translations, logout) is **proxied to Umbraco**; the wrapper only adds a small
persistent layer for its own session cookies and an **audit trail** so the whole
flow is traceable.

---

## Components

| Service    | Project                                             | Port (host) | Purpose |
|------------|-----------------------------------------------------|-------------|---------|
| `umbraco`  | `cms/Ustin.Work.LessMess.UmbarcoWrapper.Cms.csproj` | `8080`      | Umbraco 17 CMS (SQLite, unattended install). Backoffice at `/umbraco`. Exposes a small custom API under `/api/wrapper/*`. Seeds ~45 grouped dictionary items in two languages on first boot. |
| `wrapper`  | `src/Ustin.Work.LessMess.UmbarcoWrapper.Api`        | `8090`      | ASP.NET Core host: Razor Pages screens (v1/v2) **and** the member-auth JSON API + Swagger. Proxies auth to Umbraco / serves it from a local store. |

### Solution layout (`src/`)

```
src/
  Directory.Build.props / Directory.Packages.props     shared TFM + central package versions
  …Core                                                contracts, options, IMemberAuthProvider, IAuditLog — no deps
  …Infrastructure                                       JwtTokenService, Local + Proxy providers,
                                                        AddWrapperMemberAuth() (host-agnostic DI extension)
  …Infrastructure.Repository            DB A            durable store: WrapperDbContext + AuditLog (+ later
                                                        devices / licences / T&C). Registered + migrated in EVERY mode.
  …Infrastructure.LocalAuthRepository   DB B            Local-mode identity store: LocalAuthDbContext + Identity +
                                                        RefreshTokens. Registered + migrated ONLY when Mode=Local.
  …Api                                                  controllers, Razor Pages, Swagger, Program.cs
  …Worker                                               placeholder for background jobs / event handlers
```

`Api` → `Infrastructure` → { `Repository`, `LocalAuthRepository` } → `Core`.
Two Postgres databases (own DbContext, own `__EFMigrationsHistory`, own connection
string — `WrapperDb` / `LocalAuthDb`). Session state (the v1/v2 Umbraco cookie
maps) is not persisted — see below. The `Worker` will reference
`…Infrastructure.Repository` only (for the outbox), never DB B.

### Switching a deployment from Local to Proxy

1. Redeploy with `MemberAuth__Mode=Proxy`, `MemberAuth__UpstreamBaseUrl=<umbraco>`,
   `MemberAuth__Proxy__SharedSigningKey=<upstream member-JWT key>`.
2. DB A (`WrapperDb`) carries over unchanged — the audit history is kept.
3. DB B is now inert: `…LocalAuthRepository` is not registered, not migrated, not
   read. The users that lived there are unreachable (the proxy forwards to
   Umbraco; their old tokens no longer validate against the new signing key).
4. Optionally `DROP DATABASE localauth;` — leaving it is harmless. Startup logs
   `MemberAuth mode=Proxy; databases: WrapperDb (durable, A) only.`

### Umbraco custom API (`cms/Controllers`)

Plain `[ApiController]` endpoints that use Umbraco's own services:

| Endpoint | Umbraco service used | Notes |
|---|---|---|
| `POST /api/wrapper/auth/register` | `IMemberManager.CreateAsync` | creates a front-end **Member** |
| `POST /api/wrapper/auth/login` | `IMemberSignInManager.PasswordSignInAsync` | issues the member auth cookie (`Set-Cookie` on the response) |
| `POST /api/wrapper/auth/logout` | `IMemberSignInManager.SignOutAsync` | |
| `GET  /api/wrapper/auth/me` | `IMemberManager.GetCurrentMemberAsync` | 200 / 401 |
| `GET  /api/wrapper/translations` | `IDictionaryItemService` + `ILanguageService` | **requires** an authenticated member; returns the dictionary tree flattened with a `group` path |

### Wrapper session state (`src/…Api/Services`)

`InMemorySessionStore` / `InMemoryBackofficeSessionStore` — a *wrapper session id*
→ *raw Umbraco `Set-Cookie` values* map, so the wrapper can replay them as the
logged-in member on later requests. Each store has its own bounded `MemoryCache`:
a sliding `Sessions:IdleTimeout` (30 min), an absolute `Sessions:MaxLifetime`
(8 h) computed from creation so a touch can't extend it, `Sessions:MaxEntries`
as a size cap, and — for v2 — the absolute expiry clamped to the BFF cookie
set's own expiry. It lives only while the process runs; a restart signs everyone
out (the credential proof is the upstream cookie, not our state). No file, no
volume. To share sessions across instances, swap the `MemoryCache` for
`HybridCache` + a Redis `IDistributedCache` — the `ISessionStore` /
`IBackofficeSessionStore` interfaces don't change.

The **audit trail** is a separate, durable concern — see the persistence
projects below. Entries are also mirrored to `ILogger` (so `docker logs` shows
them):

  ```json
  {"ts":"2026-09-02T18:20:01.512Z","event":"login","username":"alice","ip":"172.19.0.1","userAgent":"curl/8.4","outcome":"success","detail":"umbraco member cookie stored"}
  ```

  Events: `register`, `register.failed`, `login`, `login.failed`, `logout`, `translations.list`.

---

## Run

```bash
docker compose up --build
```

* Wrapper UI: <http://localhost:8090>
* Umbraco backoffice: <http://localhost:8080/umbraco> (`Administrator` / `One-Two-Three-Four1!`)

First boot takes ~1–2 min while Umbraco runs its unattended install and the
seeder creates the dictionary items.

### Flow

1. Open <http://localhost:8090> → redirected to **/login**.
2. No account yet → **/register** → submit → redirected back to **/login** with the username pre-filled.
3. Log in → the wrapper stores the Umbraco member cookie and redirects to **/translations**.
4. **/translations** shows the ~45 seeded entries grouped by folder (`General`, `General / Buttons`, `Errors / Validation`, …) with the `en-US` and `de-DE` columns.
5. **Sign out** → Umbraco session ended, wrapper session file cleared.

Every step is written to `audit.log` with timestamp, client IP and user agent.

### Inspect the audit trail

```bash
docker compose exec wrapper cat /data/audit.log
docker compose logs wrapper | grep audit
```

---

## Two auth implementations, side by side

The wrapper ships **two independent flows** so they can be compared. They use
separate session cookies (`wrapper_sid` vs `wrapper_bo_sid`), so you can be
signed into both at once.

| | **v1 — member cookie proxy** | **v2 — back-office session (BFF)** |
|---|---|---|
| Screens | `/Login`, `/Register`, `/Translations` | `/V2/Login`, `/V2/Translations` |
| Who signs in | Umbraco **member** (front-end user) | Umbraco **back-office user** |
| Server-side code in the CMS | **yes** — custom `/api/wrapper/*` controllers | **none** — stock endpoints only |
| Mechanism | `IMemberSignInManager` issues a cookie; wrapper stores & replays it | OAuth2 Authorization Code + PKCE against Umbraco's OpenIddict server |
| Credential proof held | raw `Set-Cookie` string | the encrypted HttpOnly **cookie set** (`UMB_UCONTEXT`, `umbAccessToken`, `umbRefreshToken`) |
| Translations source | custom `GET /api/wrapper/translations` | stock `GET /umbraco/management/api/v1/dictionary` |
| Registration | supported (proxied) | n/a (back-office users are provisioned by an admin) |
| Transport | HTTP ok | OpenIddict needs HTTPS unless `Global:UseHttps=false` |
| Audit events | `register`, `login`, `translations.list`, `logout` | `backoffice.login`, `backoffice.translations.list`, `backoffice.logout` |

### Why v2 is *not* "get a JWT"

Umbraco 16.1+/17 runs the back office as a **BFF** (`HideBackOfficeTokensHandler`):
`/authorize` and `/token` put the real PKCE code and the access/refresh **JWTs
into encrypted, DataProtection-signed HttpOnly cookies** and return the literal
string `[redacted]` in their place. On the way back in, Umbraco swaps `[redacted]`
for the cookie value. So a client — including this wrapper — **cannot obtain a
usable bearer token**; it can only carry the cookie set. v2 therefore does the
full OAuth dance and then behaves like v1: it stores every `Set-Cookie` it
collected and replays them (with `Authorization: Bearer [redacted]`) on the
Management API.

**v2 flow** (all server-to-server inside the wrapper, no browser):

1. `POST …/security/back-office/login` `{username,password}` → `Set-Cookie: UMB_UCONTEXT=…`
2. `GET  …/security/back-office/authorize?client_id=umbraco-back-office&response_type=code&code_challenge=…` (with that cookie) → `302` with `code=[redacted]` + `Set-Cookie: umbPkceCode=…`
3. `POST …/security/back-office/token` (`grant_type=authorization_code`, `code=[redacted]`, `code_verifier`, PKCE cookie) → `200` with `access_token:"[redacted]"` + `Set-Cookie: umbAccessToken=… , umbRefreshToken=…`
4. Management API calls carry `Authorization: Bearer [redacted]` **and** the cookie set. Refresh: `grant_type=refresh_token`, `refresh_token=[redacted]`.

v2 sign-in in this repo uses the unattended admin: **`admin@wrapper.local` / `One-Two-Three-Four1!`**
(Umbraco sets the back-office username to the email). It does not work if the
user has 2FA or is behind an external SSO — those need the IdP, not Umbraco's form.

Local only: the compose file sets `Umbraco:CMS:Global:UseHttps=false` so OpenIddict
issues over plain HTTP. A real deployment leaves that at its default (`true`) and
fronts Umbraco with TLS; the wrapper can also trust a self-signed cert on a
staging box via `Umbraco:AllowInvalidCertificate=true`.

## Member auth API (for a mobile app) — `IMemberAuthProvider`

A JSON API at **`/api/member-auth/v1`** (register, login, `token/refresh`,
logout, `password/forgot`, `password/reset`, `password/change`, `email/confirm`,
`email/resend`, `me`) with two interchangeable implementations behind
`MemberAuth:Mode`:

| Mode | Backing | Use |
|---|---|---|
| **`Local`** (default) | self-contained identity store on **Postgres** (ASP.NET Core Identity + one `RefreshTokens` table) | testing / offline; no Umbraco needed |
| **`Proxy`** | relays every call to a real Umbraco `/api/member-auth/v1/*` | real forwarding; upstream mints the tokens |

Same request/response contract either way. Access tokens are HS256 JWT, **3 h**
lifetime (`Jwt:AccessTokenLifetime`), with rotating refresh tokens + reuse
detection. `Local` mode is refused in `Production` unless
`MemberAuth:AllowLocalInProduction=true`.

### One command → Swagger (Local mode)

```bash
docker compose -f docker-compose.local.yml up --build
```

* Swagger UI: <http://localhost:8090/swagger> — **"Ustin Provider Wrapper"**, version **1.1.0**
* Postgres: `localhost:5432` (`wrapper` / `wrapper` / db `providerwrapper`)
* EF migrations are applied on startup (with a short retry while Postgres comes up).
* `MemberAuth:ExposeTokensInResponses=true` here, so `password/forgot` returns the
  reset token in the response body (no SMTP locally).

### Proxy mode

Set `MemberAuth__Mode=Proxy`, `MemberAuth__UpstreamBaseUrl=<umbraco url>` and
`MemberAuth__Proxy__SharedSigningKey=<upstream member-JWT signing key>` (so the
wrapper can validate the tokens it forwards). Points at the
`Umbraco.Cms.MemberJwtAuth` controller.

### Known-consumer gate (`ClientGate`)

Every call to `/api/member-auth/*` must carry an `X-Client-Id` + `X-Client-Key`
pair that matches a configured client, or it gets `401 Unknown client` **before**
touching Identity / the database (`src/…Api/Security/ClientGateMiddleware.cs`).
It's a cheap filter against blind scanners and low-effort scripting, and a
revocable kill-switch (`"Disabled": true` on a client) — **not** a trust
boundary: the key ships inside the mobile app and can be read from a proxied
device. Pair it with the rate limiter and an edge WAF.

```jsonc
"ClientGate": {
  "Enabled": true,
  "ProtectedPathPrefixes": [ "/api/member-auth" ],
  "Clients": [
    { "Id": "mobile-app", "Key": "…per environment, from a secret store" }
  ]
}
```

Usage — send both headers on every request:

```bash
curl -X POST http://localhost:8090/api/member-auth/v1/login \
  -H 'Content-Type: application/json' \
  -H 'X-Client-Id: mobile-app' \
  -H 'X-Client-Key: dev-only-client-key-change-me-per-environment' \
  -d '{"usernameOrEmail":"member1@example.com","password":"Member12345!"}'
```

Local dev credentials are in `appsettings.json`; ready-to-run requests are in
`src/…Api/MemberAuth.http`. The verified client id is put on
`HttpContext.Items["ClientId"]` for logging and rate-limit partitioning. Set
`ClientGate__Enabled=false` if a service mesh already authenticates callers.

### Rate limiting (`RateLimiting`)

`AddWrapperRateLimiting` (`src/…Api/Security/RateLimitingExtensions.cs`) registers
fixed-window policies on the brute-force surfaces plus a global concurrency
backstop. Rejections return `429` + `Retry-After` + problem+json, and are logged.

| Policy / endpoints | Partition | Default |
|---|---|---|
| `auth-login` — `/login`, `/password/reset`, `/email/confirm` | client id + IP | 10 / 60 s |
| `auth-refresh` — `/token/refresh` | client id + IP | 30 / 60 s |
| `auth-forgot` — `/password/forgot`, `/email/resend` | IP | 5 / 15 min |
| `auth-register` — `/register` | IP | 5 / hour |
| global backstop — every request | — | 100 concurrent, queue 50 |

All limits are configurable per environment; `RateLimiting__Enabled=false`
disables the per-endpoint policies (the rejection handler stays). Behind more than
one instance the limiter is per-instance — move to a distributed store (Redis) or
rely on an edge WAF for a shared view. Account lockout (Identity, 5 failed →
locked) runs alongside it — a rapid wrong-password loop hits `401` × 5, then
`403` locked, then `429`.

## Logging & observability

Two separate streams — don't conflate them:

| | **audit trail** | **diagnostic logs** |
|---|---|---|
| what | `member.login`, `logout`, … — a business record | `ILogger`: info / warnings / errors / one line per request |
| store | Postgres **DB A** (`WrapperDb.AuditLog`), kept long, queried + joined | a log backend (Kibana / Loki / Seq / Sentry), kept days–weeks |
| owner | the product | ops |

### Diagnostic logs — provider-agnostic (`Serilog` section)

Serilog is the logging provider, configured **entirely from `appsettings.json`**
(`ReadFrom.Configuration`). Every event is structured and enriched with
`Application`, `MachineName`, `EnvironmentName`, `TraceId` / `SpanId`, and — on
the per-request summary line — `ClientId`. `UseSerilogRequestLogging` collapses
each request to one line. Development logs plain text; **Production emits compact
JSON to stdout** (`appsettings.Production.json`) for a shipper (Fluent Bit /
Vector / Filebeat) to forward.

Adding a backend later = a NuGet package + a `Serilog:WriteTo` block, **no code**:

```jsonc
// Seq
{ "Name": "Seq", "Args": { "serverUrl": "http://seq:5341" } }
// Elasticsearch / Kibana  (Elastic.Serilog.Sinks)
{ "Name": "Elasticsearch", "Args": { "nodes": [ "http://es:9200" ], "dataStream": "logs-wrapper" } }
// Grafana Loki  (Serilog.Sinks.Grafana.Loki)
{ "Name": "GrafanaLoki", "Args": { "uri": "http://loki:3100" } }
```

### Errors → Sentry (`Sentry` section)

`Sentry.AspNetCore` is wired (`builder.WebHost.UseSentry()`): automatic
unhandled-exception capture, HTTP breadcrumbs, release/environment tagging.
**Inert until `Sentry:Dsn` is set** — an empty DSN disables the SDK, so it ships
enabled and is switched on per environment. `LogError(ex, …)` also reaches it.

`OpenTelemetry` (OTLP → any collector) is a future option — add
`Serilog.Sinks.OpenTelemetry` as another `WriteTo` entry, or run the OTel SDK
alongside.

## Tests

* `tests/Ustin.Work.LessMess.UmbarcoWrapper.Tests` — xUnit unit tests for the
  in-memory session store, the Postgres audit log (EF InMemory), the JWT token
  service and the client-gate registry (no Docker needed): `dotnet test`.
* `tests/integration.sh` — drives the full v1/v2 stack over HTTP once
  `docker compose up` is healthy and asserts the audit trail.
