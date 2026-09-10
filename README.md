# Church Finder API

Python/FastAPI backend for a church directory and map. Stores church locations and weekly
service times, imports collector data, and provides search, filters, pagination, and map-bounds
queries. The React frontend and Google Maps integration are the next milestone.

No Google API key is required to run this backend. The collector supplies coordinates.

## Start locally

Requires Python 3.12+ and [uv](https://docs.astral.sh/uv/). PostgreSQL is the intended deployed
database; Docker Compose provides a local instance. Run commands from the repository root.

```sh
uv sync --locked
cp .env.example .env
docker compose up -d --wait db
uv run alembic upgrade head
uv run churchfinder-import data/sample_churches.json
uv run uvicorn churchfinder.main:create_app --factory --reload
```

Open [interactive API documentation](http://localhost:8000/docs).
The machine-readable specification is at [OpenAPI JSON](http://localhost:8000/openapi.json).

The sample dataset contains **three fictional churches**, including one without coordinates
or service times. It is development data, not a verified church directory. Re-running the import
updates these records without creating duplicates.

If you already have PostgreSQL, set `DATABASE_URL` to that database and skip Docker Compose.
The supplied database credentials are for local development only.

### Without Docker or PostgreSQL

SQLite is supported for a quick local demo. After `uv sync --locked`, run:

```sh
export DATABASE_URL=sqlite:///./churchfinder.db
uv run alembic upgrade head
uv run churchfinder-import data/sample_churches.json
uv run uvicorn churchfinder.main:create_app --factory --reload
```

The environment variable overrides `.env`. Clear it with `unset DATABASE_URL` to return to your
configured PostgreSQL database. Migrations are explicit; starting the API never creates tables
or imports demo data automatically.

## Configuration

| Variable | Default / format |
|---|---|
| `DATABASE_URL` | `postgresql+psycopg://churchfinder:churchfinder@localhost:5432/churchfinder` |
| `CORS_ORIGINS` | JSON array, e.g. `["http://localhost:5173","http://localhost:3000"]` |

Settings load from `.env` in the current directory, with environment variables taking precedence.
Add the deployed React origin to `CORS_ORIGINS` when the frontend is ready. Origins include the
scheme and optional port, with no trailing slash. `.env` and local databases are ignored by Git.

## API contract

All public endpoints are read-only and return JSON. No authentication is required for discovery.

| Method | Path | Response |
|---|---|---|
| GET | `/api/v1/churches` | Paginated church summaries |
| GET | `/api/v1/churches/{church_id}` | Church details, contacts, provenance, and service times |
| GET | `/api/v1/filters` | Global, distinct denominations, cities, and service languages |
| GET | `/health` | `{"status":"ok"}`; application liveness, not database readiness |

### Search and filters

| Parameter | Behavior |
|---|---|
| `q` | Case-insensitive substring of name, city, or street address; maximum 200 characters |
| `city` | Case-insensitive exact city match |
| `denomination` | Exact canonical label, including case; use values from `/filters` |
| `language` | Service language code, case-insensitive; matches at least one service |
| `bbox` | Inclusive `west,south,east,north` longitude/latitude bounds |
| `limit` | Default 50; integer from 1 to 200 |
| `offset` | Default 0; non-negative integer |

Filters combine with AND. Search input is trimmed; blank text filters are ignored. `%` and `_`
are literal characters, not search wildcards. Results sort by name, then ID. Each church appears
once even if several services match the language filter.

Bounds require `-180 <= west <= east <= 180` and `-90 <= south <= north <= 90`. Bounds crossing
the antimeridian are rejected for the MVP. Coordinates are WGS84 decimal degrees. Churches
without coordinates remain in ordinary searches but are excluded when `bbox` is supplied.

```sh
curl 'http://localhost:8000/api/v1/churches?city=Toronto&language=en&limit=10'
curl 'http://localhost:8000/api/v1/churches?bbox=-79.5,43.6,-79.3,43.8'
curl 'http://localhost:8000/api/v1/filters'
```

Example response shape (the generated ID will differ):

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000001",
      "name": "Harbour Community Church (Demo)",
      "denomination": "Non-denominational",
      "address_line1": "100 Example Street",
      "city": "Toronto",
      "region": "Ontario",
      "postal_code": null,
      "country_code": "CA",
      "latitude": 43.6532,
      "longitude": -79.3832
    }
  ],
  "total": 1,
  "limit": 10,
  "offset": 0
}
```

`total` counts matches before pagination. No matches return HTTP 200 with `items: []` and
`total: 0`. An offset beyond the last page returns an empty array while preserving the matching
total. Missing church IDs return 404 with `{"detail":"Church not found"}`. Invalid query
parameters or malformed UUIDs return FastAPI's 422 validation response, with a `detail` array
identifying the invalid field.

The detail response adds `description`, `timezone`, `website_url`, `phone`, `email`, `source`,
`source_id`, `source_url`, `last_verified_at`, `created_at`, `updated_at`, and `service_times`.
Service entries include `id`, `day_of_week`, `start_time`, `language`, `label`, and `notes`, sorted
by day, local start time, and ID. Unknown scalar values are `null`; unknown schedules are `[]`.

For React, fetch the list endpoint and use each item's ID for selection and detail requests.
Render markers only for records with non-null coordinates. A result page is not the entire map
dataset: show “Showing X of Y” and keep markers and cards on the same page. “Search this area”
can send Google Maps viewport bounds through `bbox` without changing this API.

## Data collection handoff

One record represents one physical church location or campus. Send UTF-8 JSON with a top-level
`items` array; see [the sample dataset](data/sample_churches.json) for the complete format.

Required fields are `source`, `source_id`, and `name`. All other scalar fields are optional.
`service_times` defaults to an empty array. Unknown fields are rejected to catch contract mistakes.

```json
{
  "items": [
    {
      "source": "collector",
      "source_id": "stable-location-id",
      "name": "Example Church",
      "city": "Toronto",
      "country_code": "CA",
      "latitude": 43.6532,
      "longitude": -79.3832,
      "timezone": "America/Toronto",
      "service_times": [
        {
          "day_of_week": 6,
          "start_time": "10:30",
          "language": "en",
          "label": "Sunday worship"
        }
      ]
    }
  ]
}
```

- Use a stable `(source, source_id)` pair for each location. The importer keeps its internal UUID
  on updates. Deduplication across different sources belongs to the collector.
- Each supplied record is a **complete snapshot**. Omitted optional fields are cleared and
  omitted/empty `service_times` removes the previous schedule. Churches absent from the batch
  are left unchanged. This is not a partial-update format.
- Service IDs are preserved when day, start time, language, and label are unchanged. Notes can
  be updated. Removed services are deleted, and new/changed service identities get new IDs.
- Supply latitude and longitude together or leave both null. Never use `(0, 0)` for unknown
  coordinates or substitute a city center for a verified church location.
- Days run from 0 (Monday) through 6 (Sunday). Times are local `HH:MM` or `HH:MM:SS` without a UTC
  offset. A valid IANA timezone is required when service times are present. Holiday exceptions
  and one-off events are not supported.
- Language codes are lowercase, e.g. `en`, `fr`, or `en-ca`. Country codes are two uppercase
  letters. The importer normalizes language/country case and trims text. Blank optional text
  becomes null. Agree on canonical denomination and city labels with the frontend team.
- `last_verified_at`, if supplied, must include a timezone offset, e.g. `2026-09-09T14:00:00Z`.
  It records collector verification; `updated_at` records import time. Timestamps return in UTC.
- Websites/source URLs must use HTTP or HTTPS. Email addresses must have a valid format.

Validate a handoff without needing a database, export its JSON Schema, or import it:

```sh
uv run churchfinder-import data/sample_churches.json --validate-only
uv run churchfinder-import --schema
uv run churchfinder-import path/to/collected_churches.json
```

An import returns counts such as `{"created":3,"updated":0,"total":3}`. Re-importing the same
batch reports updates, preserves church/service identity, and refreshes `updated_at`.
The entire file is validated before writing, and all writes use one transaction. Invalid files,
duplicate source identities within a batch, or database failures leave no partial import.
Run one importer at a time for the MVP; there is no public create/update/delete API.

## Tests and migrations

```sh
uv run pytest -q
uv run ruff check .
uv run ruff format --check .
```

Tests use temporary SQLite databases by default and apply the real migrations. To run the same
suite on PostgreSQL:

```sh
TEST_DATABASE_URL=postgresql+psycopg://churchfinder:churchfinder@localhost:5432/churchfinder uv run pytest -q
```

PostgreSQL tests create and remove a randomly named schema per test; they do not reset existing
application tables. The test user needs permission to create schemas.

Eight focused tests cover search/filter combinations, pagination, map bounds, church details,
duplicate-free re-imports, schedule updates, and rejection of an invalid batch before any writes.
The fixtures apply the real migrations. CI runs this small suite against both SQLite and PostgreSQL.

For future schema changes, edit the models, generate and review a migration, then apply it:

```sh
uv run alembic revision --autogenerate -m "Describe the schema change"
uv run alembic upgrade head
```

See [the original MVP ticket](church-discovery-ticket.md) for the frontend and map acceptance
criteria that follow this backend milestone.
