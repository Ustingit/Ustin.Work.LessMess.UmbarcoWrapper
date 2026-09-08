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
  Directory.Build.props / Directory.Packages.props   shared TFM + central package versions
  Ustin.Work.LessMess.UmbarcoWrapper.Core            contracts, options, IMemberAuthProvider, event types — no deps
  Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure  EF/Npgsql/Identity, JwtTokenService, Local+Proxy providers,
                                                     AddWrapperMemberAuth() (host-agnostic DI extension)
  Ustin.Work.LessMess.UmbarcoWrapper.Api             controllers, Razor Pages, Swagger, Program.cs
  Ustin.Work.LessMess.UmbarcoWrapper.Worker          placeholder for background jobs / event handlers
```

`Api` → `Infrastructure` → `Core`. `Worker` will reference `Infrastructure` and call the
same `AddWrapperMemberAuth()` when it gets real work. Migrations live in
`Infrastructure/Auth/Local/Migrations`; only the API applies them on boot.

### Umbraco custom API (`cms/Controllers`)

Plain `[ApiController]` endpoints that use Umbraco's own services:

| Endpoint | Umbraco service used | Notes |
|---|---|---|
| `POST /api/wrapper/auth/register` | `IMemberManager.CreateAsync` | creates a front-end **Member** |
| `POST /api/wrapper/auth/login` | `IMemberSignInManager.PasswordSignInAsync` | issues the member auth cookie (`Set-Cookie` on the response) |
| `POST /api/wrapper/auth/logout` | `IMemberSignInManager.SignOutAsync` | |
| `GET  /api/wrapper/auth/me` | `IMemberManager.GetCurrentMemberAsync` | 200 / 401 |
| `GET  /api/wrapper/translations` | `IDictionaryItemService` + `ILanguageService` | **requires** an authenticated member; returns the dictionary tree flattened with a `group` path |

### Wrapper persistent layer (`src/…Api/Services`)

No DB. Two files under a mounted volume (`/data`):

* `sessions.json` — map of *wrapper session id* → *raw Umbraco `Set-Cookie` values*. Lets the wrapper act as the logged-in member on later requests.
* `audit.log` — append-only JSON lines. One line per event, also mirrored to `ILogger` (so `docker logs wrapper` shows it):

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

## Tests

* `tests/Ustin.Work.LessMess.UmbarcoWrapper.Tests` — xUnit unit tests for the
  file session store, the audit log and the JWT token service (no Docker
  needed): `dotnet test`.
* `tests/integration.sh` — drives the full v1/v2 stack over HTTP once
  `docker compose up` is healthy and asserts the audit trail.
