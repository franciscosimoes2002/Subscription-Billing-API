# Subscription Billing API

A REST API for the backend of a SaaS-style product: accounts, subscription plans and subscriptions. Built with **.NET 10 (LTS)**, ASP.NET Core controllers, EF Core on SQL Server and JWT authentication.

It is a portfolio project written to show how a small backend is structured when it is meant to be operated, not just demonstrated: rules live in the domain, the database enforces the invariants that matter, failures are explicit, and every claim below is covered by a test.

> **Status:** phase 1 of 3 is done (accounts, plans, subscriptions, tests, CI), plus health endpoints and Docker packaging. Invoices (PDF), payment simulation and rate limiting are **not built yet** and are listed under [Roadmap](#roadmap). Nothing in this README describes a feature that does not exist.

## What it does

| Area | Behaviour |
|---|---|
| Accounts | Register, log in, read your own profile. Passwords are hashed with PBKDF2-HMAC-SHA512 (210,000 iterations, per-password salt). |
| Plans | Public catalogue. Administrators create, update and (soft) delete plans. |
| Subscriptions | A user subscribes to a plan, lists and cancels their own subscriptions. One active subscription per user. |

### Endpoints

All routes are under `/api/v1`. Anything not marked public needs `Authorization: Bearer <token>`.

| Method and route | Access | Notes |
|---|---|---|
| `POST /auth/register` | public | Always creates the `User` role. |
| `POST /auth/login` | public | Returns a 15-minute bearer token. |
| `GET /auth/me` | user | |
| `GET /plans`, `GET /plans/{id}` | public | Only plans that are on sale. Paginated. |
| `GET /plans/all` | admin | Includes deactivated plans. |
| `POST /plans`, `PUT /plans/{id}`, `DELETE /plans/{id}` | admin | Delete is soft. |
| `POST /subscriptions` | user | Body: `{ "planId": "..." }`. |
| `GET /subscriptions`, `GET /subscriptions/{id}` | user | Only the caller's own. |
| `POST /subscriptions/{id}/cancel` | user | |

`GET /health` (liveness) and `GET /ready` (the database answers; `503` otherwise) are public, sit outside `/api/v1` and never say why something is not ready.

The OpenAPI document is served at `/openapi/v1.json` in Development (or when `OpenApi:Enabled=true`).

## Architecture

```
┌────────────┐   ┌───────────────┐   ┌──────────┐
│    Api     │──▶│  Application  │──▶│  Domain  │
│ controllers│   │ use cases,    │   │ entities,│
│ auth, HTTP │   │ ports, DTOs   │   │ rules    │
└─────┬──────┘   └───────▲───────┘   └──────────┘
      │                  │ implements ports
      │          ┌───────┴────────┐
      └─────────▶│ Infrastructure │  EF Core, SQL Server, password hashing
                 └────────────────┘
```

Dependencies point inwards: `Domain` has no references, `Application` knows only `Domain`, and the web and persistence details sit outside. The application services are tested with in-memory ports, with no database and no web server.

```
src/
  SubscriptionBilling.Domain          entities and business rules
  SubscriptionBilling.Application     use cases, repository and service ports, DTOs
  SubscriptionBilling.Infrastructure  EF Core model, migrations, repositories, PBKDF2 hasher
  SubscriptionBilling.Api             controllers, JWT, error handling, middleware
tests/
  SubscriptionBilling.Domain.Tests           unit tests: domain rules and application services
  SubscriptionBilling.Api.IntegrationTests   the real HTTP pipeline against SQLite
```

## Design decisions

- **The database guarantees the important rule.** "One active subscription per user" is a filtered unique index (`UX_Subscriptions_OneActivePerUser`), not only an `if` in the code. The application checks first for a friendly error, and when two concurrent requests both pass the check, the index lets exactly one through and the other becomes a `409`. A test fires six requests at once and expects exactly one `201`.
- **Money is an integer.** Prices are stored in cents (`long`) with an ISO 4217 currency code. There are no floating-point amounts anywhere.
- **History is not rewritten.** A subscription copies the plan name, price, currency and interval when it starts. Editing or deleting the plan later does not change what existing subscribers agreed to.
- **Soft delete with a global query filter.** Deleted rows disappear from every query. Unique indexes only cover live rows, so a deleted plan frees its name.
- **Expected failures are values, not exceptions.** Use cases return `Result<T>` with a stable error code. Only broken business invariants throw, and a single exception handler maps them to `400`/`409`.
- **Everything is UTC.** Timestamps are `DateTime` in UTC, enforced by the entities and restored by a value converter on read.
- **Time is injected.** Services take a `TimeProvider`, and the domain takes the current time as an argument, so rules about periods are tested with fixed dates.

## Security

- **Default deny.** A fallback authorization policy requires an authenticated user everywhere; public endpoints say `[AllowAnonymous]` explicitly. A side effect, covered by a test: an anonymous caller gets `401` even for routes that do not exist, so routes cannot be probed.
- **Authorization is proven per endpoint**, with tests for anonymous, ordinary user and administrator on the plan endpoints.
- **Ownership is part of the query.** Subscriptions are looked up by `(userId, subscriptionId)`. Someone else's subscription is a `404`, never a `403`, so ids cannot be enumerated.
- **No user enumeration on login.** An unknown email and a wrong password return the identical response, and an unknown account still spends the same hashing work.
- **Strict input handling.** Unknown JSON fields are rejected (nobody can send `"role": "Admin"` to register), enums do not accept integers, request bodies are capped at 64 KB, page sizes are capped at 100, and passwords are length-checked (12 to 128 characters).
- **Tokens.** HS256, 15-minute lifetime, issuer and audience validated, only the expected algorithm accepted, 30-second clock skew. The signing key is never in the repository; startup fails if it is missing or shorter than 32 characters.
- **Errors never leak internals.** Unhandled exceptions become a generic `500` with a correlation id; the stack trace goes to the log only.
- **Hygiene.** Responses carry `X-Content-Type-Options`, `Referrer-Policy`, `Cache-Control: no-store` and a restrictive CSP; the `Server` header is removed. Logs are structured, use message templates, and never contain emails or passwords. Every request has a correlation id (an incoming `X-Correlation-Id` is accepted only if it looks like an id).
- **Dependencies.** The NuGet vulnerability audit is on and warnings are errors, so a vulnerable package fails the build.

TLS is expected to terminate at a reverse proxy or the hosting platform, which should also add `Strict-Transport-Security`.

## Running it locally

You need the .NET 10 SDK and a SQL Server instance (SQL Server, SQL Server in a container, or LocalDB on Windows). Secrets go in user-secrets, never in the repository:

```bash
cd src/SubscriptionBilling.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=SubscriptionBilling;User Id=sa;Password=<your password>;TrustServerCertificate=True"
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 random characters>"
dotnet user-secrets set "Database:Initialization" "Migrate"      # applies EF Core migrations at startup
dotnet user-secrets set "Seed:AdminEmail" "admin@example.com"    # optional first administrator
dotnet user-secrets set "Seed:AdminPassword" "<12+ characters>"
dotnet run
```

The API listens on `http://localhost:5236`. A first run:

```bash
curl -s -X POST localhost:5236/api/v1/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"admin@example.com","password":"<12+ characters>"}'
# then: Authorization: Bearer <accessToken> on POST /api/v1/plans, and so on
```

### With Docker

Needs only Docker. The multi-stage image runs as a non-root user and listens on port 8080; Compose adds SQL Server and applies the migrations on start.

```bash
cp .env.example .env          # then fill in MSSQL_SA_PASSWORD, JWT_SIGNING_KEY and the seed admin
docker compose up --build
curl -s localhost:8080/ready  # {"status":"ready"}
```

Compose refuses to start while `MSSQL_SA_PASSWORD` or `JWT_SIGNING_KEY` is empty, and `.env` is git-ignored.

### Configuration

| Setting | Required | Purpose |
|---|---|---|
| `ConnectionStrings:Default` | yes | SQL Server connection string. |
| `Jwt:SigningKey` | yes | At least 32 characters. |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenMinutes` | no | Defaults in `appsettings.json`. |
| `Database:Initialization` | no | `None` (default), `Migrate` or `EnsureCreated`. |
| `Seed:AdminEmail`, `Seed:AdminPassword` | no | Creates the first administrator when both are set. |
| `OpenApi:Enabled` | no | Serves `/openapi/v1.json` outside Development. |

## Tests

```bash
dotnet test
```

74 tests run in a few seconds. The integration suite boots the real application (middleware, authentication, controllers, EF Core) against a throw-away SQLite file, so it needs no external service. The production database is SQL Server, so the suite avoids provider-specific SQL; the migration itself is generated for SQL Server.

CI (`.github/workflows/ci.yml`) checks formatting, builds in Release with warnings as errors and runs the tests on every push and pull request.

## Roadmap

Not built yet, in this order:

1. Payments and invoices: a simulated payment gateway behind an interface, idempotency keys on state-changing requests, invoice history and PDF invoices.
2. Rate limiting on login and registration.
3. A CI job that runs the integration suite against SQL Server.

Out of scope on purpose: refresh tokens, email verification, password reset and real payment providers.

## License

MIT
