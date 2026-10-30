# Contributing

Thank you for helping. Issues and pull requests are welcome, in English.

## Layout

| Path                     | Content                                                                 |
| ------------------------ | ----------------------------------------------------------------------- |
| `backend/`               | .NET 10 solution: `Noosphere.Domain`, `Noosphere.Api` (API and CLI), tests |
| `frontend/`              | React 19 + Vite + TypeScript single-page application                   |
| `docs/`                  | User documentation (`en/`, `fr/`), OpenAPI copy, exchange format schema |
| `Dockerfile`, `docker-compose.yml` | Container image and local stack                               |

## Backend

Requirements: .NET SDK 10 and PostgreSQL 16.

```sh
cd backend
dotnet build                      # warnings are errors
dotnet test                       # needs PostgreSQL, see below
cd src/Noosphere.Api && NOOSPHERE_DEMO=true dotnet run   # http://localhost:5080
```

The Development settings use `Host=localhost;Database=noosphere;Username=postgres;Password=postgres`. Integration
tests create and drop a `noosphere_tests` database on the server given by `TEST_PG_SERVER` (default
`Host=localhost;Port=5432;Username=postgres;Password=postgres`).

Schema changes go through an EF Core migration:

```sh
cd backend
dotnet tool restore
dotnet ef migrations add <Name> --project src/Noosphere.Api --output-dir Data/Migrations
```

When the API contract changes, refresh the committed OpenAPI document and the frontend types:

```sh
cd backend && UPDATE_OPENAPI=1 dotnet test --filter OpenApi
cd ../frontend && npm run api:types
```

## Frontend

```sh
cd frontend
npm ci
npm run dev
npm run lint && npm run format:check && npm test && npm run build
```

`npm run dev` proxies `/api` to the API on <http://localhost:5080>; set `NOOSPHERE_API_URL` to use another address.
`npm run dev:mock` runs the interface on an in-browser mock of the API instead (demo accounts alice, bob, david and
claire, password `demo`); production builds never include it.

## Conventions

- Code, API, CLI and exchange format keys are in English. The interface and the documentation are in English and
  French, English first: update both `docs/en/` and `docs/fr/`.
- Small commits with clear messages; a pull request explains why.
- Every change comes with tests; CI must be green.
