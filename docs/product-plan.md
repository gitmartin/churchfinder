# ChurchFinder Canada — product proposal

> This proposal records the broader product direction and an earlier stack suggestion.
> The current backend uses Python/FastAPI; see [the README](../README.md) for the implemented API and setup.

A web app for finding a church in Canada. Users search on a map, narrow the results
with filters, and see enough about each church — reviews, website, latest sermon — to
decide whether to visit on Sunday.

## The problem

Finding a church in a new city is hard. Denominational directories are fragmented and
often out of date, Google Maps shows you the building but not what happens inside, and
nothing lets you answer the questions people actually ask: *Is it my tradition? Is it big
or small? Is it a century-old parish or a five-year-old church plant? What's the preaching
like?*

## Core features

### Map-based location search
- Interactive map of Canada; churches appear as pins in the current viewport
- Pan and zoom to re-query, or search by city, address, or postal code
- "Near me" using browser geolocation
- Distance radius control (e.g. within 5 / 10 / 25 km)
- Results list stays in sync with the map — hover a pin, highlight the card

### Filters
- **Denomination** — Catholic, Anglican, United, Baptist, Pentecostal, Presbyterian,
  Lutheran, Orthodox, non-denominational, and others; multi-select
- **Size** — by typical weekly attendance, bucketed (under 100, 100–500, 500–2000, 2000+)
- **Age** — by year founded, bucketed (pre-1900, 1900–1950, 1950–2000, 2000+)
- Filters combine, and are reflected in the URL so a filtered view can be shared

### Church detail
- **Google reviews** — rating, review count, and the most recent review text
- **Website link** — straight through to the church's own site
- **Most recent sermon** — embedded or linked, pulled from the church's YouTube channel
  or podcast feed, with the title and date
- Service times, address, contact info, photos

## Data model (sketch)

```
Church
  id, name, denomination, address, city, province, postal_code
  latitude, longitude
  year_founded            -> powers the age filter
  attendance_estimate     -> powers the size filter
  website_url
  google_place_id         -> key for fetching reviews on demand
  sermon_feed_url         -> YouTube channel or podcast RSS
  service_times
```

## Data sourcing — the hard part

This is the main open question, and it shapes the rest of the build.

| Field | Where it comes from | Difficulty |
|---|---|---|
| Name, address, coordinates | Google Places API | Easy |
| Reviews, rating, photos | Google Places API, by `place_id` | Easy |
| Website | Google Places API | Easy |
| Denomination | Denominational directories, or inferred from the name | Medium |
| **Size (attendance)** | **No public source** — needs self-reporting or estimation | **Hard** |
| **Age (year founded)** | **No public source** — needs per-church research | **Hard** |
| Most recent sermon | Crawl the church website for a YouTube channel or podcast RSS | Medium |

Google Places will get us a map full of real churches almost immediately, but it knows
nothing about denomination, size, or founding year — the three filters that make this app
more useful than Google Maps. Those have to come from somewhere else: a seeded dataset we
curate, crawlers over denominational church-finders, or eventually churches claiming and
filling in their own listings.

A reasonable path: **seed a curated dataset for a few major metros** so all three filters
genuinely work, pull reviews and websites live from Places, and expand coverage from there.

## Proposed stack

- **Frontend** — Next.js (App Router) + TypeScript + Tailwind
- **Map** — Google Maps JS API (pairs naturally with Places for reviews), or
  MapLibre + OpenStreetMap if we want to avoid Maps billing
- **Backend** — Next.js route handlers
- **Database** — Postgres with PostGIS for geospatial queries (radius, viewport bounds)
- **External APIs** — Google Places (reviews, website, photos), YouTube Data API and
  podcast RSS parsing (sermons)
- **Hosting** — Vercel, with a managed Postgres

## Rough plan

1. Project scaffold, map rendering, pins from a hardcoded list
2. Postgres + PostGIS schema, viewport and radius queries
3. Seed a curated dataset with denomination, size, and age filled in
4. Filter UI wired to the query, with filter state in the URL
5. Church detail panel — Places reviews, website link, photos
6. Sermon ingestion from YouTube channels and podcast feeds
7. Polish: mobile layout, loading and empty states, shareable URLs

## Open questions

- How do we get size and founding year at scale? Curate, crawl, or let churches claim
  their listing?
- Should churches be able to edit their own entries? That implies auth and moderation.
- How is "size" defined — weekly attendance, membership, or building capacity?
- What does a church with no website and no sermon feed look like in the UI?
- Bilingual support for Quebec — is French a launch requirement?
