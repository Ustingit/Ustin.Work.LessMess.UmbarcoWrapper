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
| `wrapper`  | `src/Ustin.Work.LessMess.UmbarcoWrapper.Web.csproj` | `8090`      | ASP.NET Core Razor Pages app. 3 screens: **login**, **register**, **translations**. Proxies auth to Umbraco, forwards the Umbraco member cookie on subsequent calls. |

### Umbraco custom API (`cms/Controllers`)

Plain `[ApiController]` endpoints that use Umbraco's own services:

| Endpoint | Umbraco service used | Notes |
|---|---|---|
| `POST /api/wrapper/auth/register` | `IMemberManager.CreateAsync` | creates a front-end **Member** |
| `POST /api/wrapper/auth/login` | `IMemberSignInManager.PasswordSignInAsync` | issues the member auth cookie (`Set-Cookie` on the response) |
| `POST /api/wrapper/auth/logout` | `IMemberSignInManager.SignOutAsync` | |
| `GET  /api/wrapper/auth/me` | `IMemberManager.GetCurrentMemberAsync` | 200 / 401 |
| `GET  /api/wrapper/translations` | `IDictionaryItemService` + `ILanguageService` | **requires** an authenticated member; returns the dictionary tree flattened with a `group` path |

### Wrapper persistent layer (`src/Services`)

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

## Tests

* `tests/Ustin.Work.LessMess.UmbarcoWrapper.Web.Tests` — xUnit unit tests for the
  file session store and the audit log (no Docker needed): `dotnet test`.
* `tests/integration.sh` — drives the full stack over HTTP once `docker compose up`
  is healthy and asserts the audit trail contains the expected events.
