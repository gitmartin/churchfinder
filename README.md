# ChurchFinder Canada

React frontend and Python/FastAPI backend for a church directory and Google Map. Search by name,
city, denomination, or service language, select a church in the list or map, and view its service
times and contact details. The same frontend can be embedded in another website.

The collector supplies coordinates. Google Maps uses the browser key in `GOOGLE_MAPS_API`;
the read API itself works without that key.

## Run the frontend and backend together

Requires Python 3.12+, [uv](https://docs.astral.sh/uv/), and Node.js 22.12+.
Copy `.env.example` to `.env` if you have not already configured it, then set `GOOGLE_MAPS_API`.
Keep an existing `.env` with your key; do not overwrite it.

```sh
bash scripts/run-local.sh
```

The script installs dependencies, builds the frontend, migrates a separate local SQLite database
(`churchfinder-map.db`), imports the ten Toronto research records, and starts both servers:

- [React frontend](http://127.0.0.1:5173/) — live reload while editing the UI.
- [Backend and compiled frontend](http://127.0.0.1:8000/) — the standalone production build.
- [API documentation](http://127.0.0.1:8000/docs).
- [Embedding demo and copyable snippet](http://127.0.0.1:8000/embed-demo.html).

Nine records have coordinates; the tenth stays in the list with “Map location unavailable”.
Click a card or pin for details. “Search this area” applies the current map bounds. Map pins and
cards use the same page of results, and the UI shows when there are more matches.

The script uses SQLite even if `.env` points to PostgreSQL. To use your own database instead,
export `DATABASE_URL` before starting it. Ctrl+C stops both servers. Backend edits require a
restart; React edits reload automatically. Re-running the script safely updates the seed records.

The browser key is fetched from `/api/v1/config` at runtime; it is not baked into the frontend
bundle. That endpoint returns only the Maps key and map ID. Restrict the key to Maps JavaScript
API and the frontend origins in Google Cloud, including your local preview origin during
development. Use your own `GOOGLE_MAPS_MAP_ID` for deployment; `DEMO_MAP_ID` works for testing.

## Project direction and research

See the [product proposal](docs/product-plan.md) for the broader vision, planned frontend
features, and open questions. The [data-sourcing research](docs/data-sourcing.html) and
[Toronto research seed](data/seed-toronto-10.json) are also available. Import the research seed
with `uv run churchfinder-import data/seed-toronto-10.json --format research`. This maps its
top-level source and church records to the API schema; extra research metadata stays in the JSON.
Coordinates from this dataset are credited to OpenStreetMap in the frontend.

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
| `GOOGLE_MAPS_API` | Browser key for Maps JavaScript API; blank disables the map but keeps the list |
| `GOOGLE_MAPS_MAP_ID` | `DEMO_MAP_ID` for testing, or a custom JavaScript map ID |
| `EMBED_ALLOWED_ORIGINS` | JSON array of exact host origins; defaults to `[]` (same-origin only) |
| `FRONTEND_DIST` | Optional path to compiled assets; defaults to `frontend/dist` in this checkout |

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

Ten focused tests cover the core API/import behavior, the Toronto seed adapter, and the public
frontend configuration/embedding policy. The fixtures apply the real migrations. CI runs this
small suite against both SQLite and PostgreSQL and compiles the React frontend.

For future schema changes, edit the models, generate and review a migration, then apply it:

```sh
uv run alembic revision --autogenerate -m "Describe the schema change"
uv run alembic upgrade head
```

See [the original MVP ticket](church-discovery-ticket.md) for the full MVP scope.

## Embed in another website

The reusable loader follows the iframe mount/ready/destroy pattern found in the Faith.UI project
on the repository's `master` branch. At implementation time, `Distro` contained only the original
ticket. This is a React/Python implementation with its own `ChurchFinder` namespace; it does not
require the .NET service or its SSE resize connections. The map uses a fixed configurable height
and fills its container width, with internal scrolling for results and details.

```html
<div id="church-map"></div>
<script src="https://your-churchfinder.example/churchfinder.js"></script>
<script>
  const widget = ChurchFinder.mount("#church-map", {
    endpoint: "https://your-churchfinder.example/embed/churches",
    initialHeight: 760,
    city: "Toronto"
  });
  widget.ready.catch(error => console.error(error.message));
  // When removing the component: widget.destroy();
</script>
```

Options: `endpoint`, `initialHeight` (480–2400 px), `title`, and initial `q`, `city`, `denomination`,
or `language` filters. `ready` resolves after the map and results load, or rejects on failure or
timeout. Messages are checked against the iframe window, origin, and instance ID. `destroy()`
removes the frame and event listener. `ChurchFinder.getEmbedCode()` returns a basic snippet.

A plain iframe also works:

```html
<iframe src="https://your-churchfinder.example/embed/churches?city=Toronto"
        title="Find a church" style="width:100%;height:760px;border:0"></iframe>
```

Add the parent site's exact origin to `EMBED_ALLOWED_ORIGINS`, e.g. `["https://customer.example"]`.
FastAPI enforces this with the `frame-ancestors` response header. The parent site must permit the
ChurchFinder origin in its own `script-src` and `frame-src` policy, if it has one. API calls stay
inside the iframe, so embedding does not require adding the parent to API CORS. Keep the Maps key
restricted to the ChurchFinder origin, where the iframe actually runs.

To try a separate-origin host while the local servers are running:

```sh
python3 -m http.server 5242 --bind 127.0.0.1 --directory examples/embed-host
```

Open [the independent host](http://127.0.0.1:5242/). `run-local.sh` allows these demo origins by
default. Production startup uses the origins explicitly configured in `.env` or the environment.

For deployment with the existing Python service, run `npm ci --prefix frontend` and
`npm run build --prefix frontend`, then run the API. FastAPI serves the compiled app at `/`, the
widget at `/embed/churches`, and the loader at `/churchfinder.js`. Deploy the repo with the build
output and run migrations/imports as appropriate; no separate frontend hosting is required.
