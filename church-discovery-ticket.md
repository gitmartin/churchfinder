# Ticket: Build church discovery API and React map integration

**User story:** As someone looking for a church, I want to search and filter churches, see their locations on a map, and view service times and contact details so I can decide where to visit.

**Proposed stack:** Python/FastAPI, PostgreSQL, and React with Google Maps JavaScript API.

**Data flow:** Collected data → database → Python API → React results list and Google Maps markers.

Data collection and converting addresses into coordinates are owned by the data collection teammate. This ticket owns the storage contract, read API, and frontend integration.

**Current milestone:** The Python API, database migrations, collector import, and backend tests are implemented. React and Google Maps integration remain a separate follow-up. See [the README](README.md) for setup and the data handoff contract.

## 1. Create the basic schema

Treat each physical church location or campus as one church record.

### `churches`

| Field | Type | Notes |
|---|---|---|
| `id` | UUID, primary key | Stable internal identifier |
| `source` | Text | Data source name |
| `source_id` | Text | Stable location identifier from the collector |
| `name` | Text | Required |
| `denomination` | Text, nullable | Agreed, normalized label |
| `description` | Text, nullable | Short overview |
| `address_line1` | Text, nullable | Street address |
| `city`, `region`, `postal_code`, `country_code` | Text, nullable | Separate address fields |
| `latitude`, `longitude` | Double precision, nullable | Both present or both absent |
| `timezone` | Text, nullable | IANA timezone, e.g. `America/Toronto` |
| `website_url`, `phone`, `email` | Text, nullable | Public contact information |
| `source_url` | Text, nullable | Original listing or source |
| `last_verified_at` | Timestamp, nullable | When the collector last checked the information |
| `created_at`, `updated_at` | Timestamp | Managed by the system |

### `service_times`

A church can have multiple recurring weekly services.

| Field | Type | Notes |
|---|---|---|
| `id` | UUID, primary key | Service identifier |
| `church_id` | UUID, foreign key | References `churches.id` |
| `day_of_week` | Integer | 0 = Monday through 6 = Sunday |
| `start_time` | Time | Local time in the church’s timezone |
| `language` | Text, nullable | Normalized language code, e.g. `en` |
| `label` | Text, nullable | E.g. “Sunday worship” |
| `notes` | Text, nullable | Additional schedule information |

Implementation rules:

- Enforce uniqueness on `(source, source_id)` so repeated imports update existing records.
- Collector owns deduplication across different sources.
- Validate latitude between −90 and 90 and longitude between −180 and 180.
- Use `null` for unknown information; never invent coordinates or service times.
- Records without coordinates remain searchable but have no map marker.
- Require a timezone when structured service times are supplied.
- Add indexes on `city`, `denomination`, and `service_times.church_id`.
- For the MVP, support weekly schedules; holiday exceptions are out of scope.

## 2. Implement the Python API

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/churches` | Search, filter, and paginate churches |
| `GET /api/v1/churches/{id}` | Return complete church details and service times |
| `GET /api/v1/filters` | Return available denominations, cities, and service languages |
| `GET /health` | Basic application health check |

Supported query parameters for `GET /api/v1/churches`:

| Parameter | Behavior |
|---|---|
| `q` | Case-insensitive substring search across name, city, and street address |
| `city` | Exact city filter, case-insensitive |
| `denomination` | Exact normalized denomination filter |
| `language` | Match churches with at least one service in that language |
| `bbox` | Optional `west,south,east,north` map bounds |
| `limit` | Default 50, maximum 200 |
| `offset` | Default 0 |

Combine supplied filters with AND. Return each church once, even if multiple services match. Sort by name, then ID for stable pagination.

Return this response envelope:

```json
{
  "items": [],
  "total": 0,
  "limit": 50,
  "offset": 0
}
```

`total` is the matching count before pagination. Each list item includes ID, name, denomination, address fields, latitude, and longitude. The detail endpoint additionally returns description, contacts, timezone, service times, and verification information.

Additional requirements:

- Apply map bounds before pagination; exclude records without coordinates when bounds are supplied.
- Validate bounds and pagination. For the MVP, reject bounds crossing the antimeridian.
- Return `200` with an empty array for no matches, `404` for an unknown church, and `422` for invalid parameters.
- Document request and response models in FastAPI’s API documentation.
- Configure CORS for the React development and deployed origins.
- Provide database migrations, sample records, and a documented JSON import command matching the collector’s handoff format. No public write API is required.

## 3. Connect the React frontend

Implement search/filter controls, a results list, a Google Map, and a church detail panel.

Expected behavior:

- On load, fetch the first results page and show cards plus markers for records with coordinates.
- Debounce text search by approximately 300 ms; reset pagination when filters change.
- Use the same returned records for the list and map. Clearly show “Showing X of Y results” so a partial page is not presented as every match.
- Changing pages updates both cards and markers.
- Clicking a marker selects its card and opens its details.
- Clicking a card opens details and focuses its marker when coordinates exist.
- After panning or zooming, offer **Search this area**. Clicking it sends the current map bounds to the API.
- Keep map positioning stable while browsing results; provide **Clear area filter** to return to unrestricted search.
- Display service times in the church’s local timezone and provide website/contact links.
- Handle loading, empty results, API errors, missing information, and map-loading failure. Keep the list usable if the map fails.
- Cancel or ignore outdated search responses.

Configure a billing-enabled Google Cloud project, Maps JavaScript API, browser API key, and map ID. Use Advanced Markers; `DEMO_MAP_ID` is available for testing. [Google setup documentation](https://developers.google.com/maps/documentation/javascript/advanced-markers/start).

Restrict the browser key to approved website origins and the Maps JavaScript API. Supply configuration through environment variables; the browser key remains visible to users. [Google key restrictions](https://developers.google.com/maps/api-security-best-practices).

## 4. Acceptance criteria

- [x] Sample data imports successfully; importing it again creates no duplicate churches.
- [x] Search and combined filters return the expected unique records.
- [x] Pagination and matching totals are correct.
- [ ] “Search this area” returns only churches inside the selected bounds.
- [ ] List cards and markers represent the same result page, except records missing coordinates.
- [ ] Selecting a card or marker displays the correct church details and service times.
- [ ] Missing coordinates and optional fields do not break the UI.
- [ ] Empty results, invalid requests, unknown IDs, and loading failures are handled.
- [x] API tests cover filtering, bounds, pagination, validation, and detail lookup.
- [ ] An end-to-end smoke check covers search → marker selection → church details.
- [x] README documents startup, environment variables, data handoff format, and sample requests.

## Out of scope

Scraping, geocoding, accounts, reviews, favorites, public editing, recommendations, travel-time routing, and holiday schedule exceptions.
