import { useCallback, useEffect, useState } from "react";
import { ChurchMap } from "./ChurchMap";
import { address, getJSON, hasCoordinates, type ChurchDetail, type Filters, type MapConfig, type Page } from "./types";

const initialParams = new URLSearchParams(window.location.search);
const embedded = window.location.pathname.startsWith("/embed/");
const days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
const limit = 50;

function notifyHost(type: "ready" | "error") {
  const parentOrigin = initialParams.get("parentOrigin");
  const instanceId = initialParams.get("instanceId");
  if (!embedded || !parentOrigin || !instanceId || window.parent === window) return;
  try {
    const origin = new URL(parentOrigin);
    if (!["http:", "https:"].includes(origin.protocol) || origin.origin !== parentOrigin) return;
    window.parent.postMessage({ type: `churchfinder:${type}`, instanceId }, parentOrigin);
  } catch { /* Invalid parent parameters do not affect standalone rendering. */ }
}

export default function App() {
  const [query, setQuery] = useState(initialParams.get("q") || "");
  const [search, setSearch] = useState(query);
  const [city, setCity] = useState(initialParams.get("city") || "");
  const [denomination, setDenomination] = useState(initialParams.get("denomination") || "");
  const [language, setLanguage] = useState(initialParams.get("language") || "");
  const [bbox, setBbox] = useState("");
  const [offset, setOffset] = useState(0);
  const [page, setPage] = useState<Page | null>(null);
  const [filters, setFilters] = useState<Filters | null>(null);
  const [config, setConfig] = useState<MapConfig | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<ChurchDetail | null>(null);
  const [detailError, setDetailError] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  const [mapStatus, setMapStatus] = useState<"ready" | "error" | null>(null);
  const reportMapStatus = useCallback((status: "ready" | "error") => setMapStatus(status), []);

  useEffect(() => {
    const timer = setTimeout(() => { setSearch(query); setOffset(0); }, 300);
    return () => clearTimeout(timer);
  }, [query]);

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([
      getJSON<MapConfig>("/api/v1/config", controller.signal),
      getJSON<Filters>("/api/v1/filters", controller.signal),
    ]).then(([nextConfig, nextFilters]) => {
      setConfig(nextConfig);
      setFilters(nextFilters);
    }).catch(() => {
      if (!controller.signal.aborted) setError("Couldn't connect. Check your connection and try again.");
    });
    return () => controller.abort();
  }, [retry]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError("");
    setSelectedId(null);
    const params = new URLSearchParams({ limit: String(limit), offset: String(offset) });
    if (search) params.set("q", search);
    if (city) params.set("city", city);
    if (denomination) params.set("denomination", denomination);
    if (language) params.set("language", language);
    if (bbox) params.set("bbox", bbox);
    getJSON<Page>(`/api/v1/churches?${params}`, controller.signal)
      .then(setPage)
      .catch((err: Error) => {
        if (!controller.signal.aborted) { setError(err.message); setPage(null); }
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [search, city, denomination, language, bbox, offset, retry]);

  useEffect(() => {
    setDetail(null);
    setDetailError("");
    if (!selectedId) return;
    const controller = new AbortController();
    getJSON<ChurchDetail>(`/api/v1/churches/${encodeURIComponent(selectedId)}`, controller.signal)
      .then(setDetail)
      .catch((err: Error) => { if (!controller.signal.aborted) setDetailError(err.message); });
    document.getElementById(`church-${selectedId}`)?.scrollIntoView({ block: "nearest" });
    return () => controller.abort();
  }, [selectedId]);

  useEffect(() => {
    if (error || mapStatus === "error") notifyHost("error");
    else if (page && mapStatus === "ready") notifyHost("ready");
  }, [page, mapStatus, error]);

  const churches = page?.items || [];
  const unmapped = churches.filter((church) => !hasCoordinates(church)).length;
  const hasFilters = Boolean(query || city || denomination || language || bbox);
  const languageName = (code: string) => {
    try { return new Intl.DisplayNames(["en"], { type: "language" }).of(code) || code; }
    catch { return code; }
  };
  function clearFilters() {
    setQuery(""); setSearch(""); setCity(""); setDenomination(""); setLanguage(""); setBbox(""); setOffset(0);
  }

  return (
    <main className={embedded ? "app embedded" : "app"}>
      <header><div className="brand"><span className="brand-mark" aria-hidden="true">✝</span><h1>ChurchFinder <span>Canada</span></h1></div>{!embedded && <a href="/embed-demo.html">Embed this map ↗</a>}</header>
      <form className="filters" onSubmit={(event) => { event.preventDefault(); setSearch(query); setOffset(0); }}>
        <label className="search-label">Search churches<input type="search" placeholder="Name, city, or address" value={query} onChange={(event) => setQuery(event.target.value)} /></label>
        <label>City<select value={city} onChange={(event) => { setCity(event.target.value); setOffset(0); }}><option value="">All cities</option>{filters?.cities.map((value) => <option key={value}>{value}</option>)}</select></label>
        <label>Denomination<select value={denomination} onChange={(event) => { setDenomination(event.target.value); setOffset(0); }}><option value="">All denominations</option>{filters?.denominations.map((value) => <option key={value}>{value}</option>)}</select></label>
        <label>Service language<select value={language} onChange={(event) => { setLanguage(event.target.value); setOffset(0); }}><option value="">All languages</option>{filters?.languages.map((value) => <option key={value} value={value}>{languageName(value)}</option>)}</select></label>
        {bbox && <button type="button" onClick={() => { setBbox(""); setOffset(0); }}>Clear area filter</button>}
        {hasFilters && <button type="button" className="clear-filters" onClick={clearFilters}>Reset</button>}
      </form>
      {error && <div className="error" role="alert">{error} <button onClick={() => setRetry(retry + 1)}>Try again</button></div>}
      <div className="workspace">
        <aside className="results" aria-label="Church results" aria-busy={loading}>
          <div className="result-count" role="status"><strong>{loading ? "Loading churches…" : `${page?.total || 0} churches found`}</strong>{!loading && <small>{churches.filter(hasCoordinates).length} on this map{unmapped > 0 ? ` · ${unmapped} location unavailable` : ""}{page && page.total > churches.length ? ` · showing ${churches.length} of ${page.total}` : ""}</small>}</div>
          <div className="church-list">
            {churches.map((church) => <button id={`church-${church.id}`} key={church.id} className={`church-card ${selectedId === church.id ? "selected" : ""}`} onClick={() => setSelectedId(church.id)} aria-pressed={selectedId === church.id}>
              <strong>{church.name}</strong><span>{church.denomination || "Denomination not listed"}</span><span>{address(church)}</span>{!hasCoordinates(church) && <small>Map location unavailable</small>}
            </button>)}
            {!loading && !error && churches.length === 0 && <p className="empty">No churches match. Try clearing a filter or searching a different area.</p>}
          </div>
          {page && page.total > limit && <nav className="pagination" aria-label="Results pages"><button disabled={loading || offset === 0} onClick={() => setOffset(Math.max(0, offset - limit))}>Previous</button><span>{Math.floor(offset / limit) + 1}</span><button disabled={loading || offset + limit >= page.total} onClick={() => setOffset(offset + limit)}>Next</button></nav>}
        </aside>
        {config ? <ChurchMap config={config} churches={churches} selectedId={selectedId} onSelect={setSelectedId} onSearchArea={(value) => { setBbox(value); setOffset(0); }} onStatus={reportMapStatus} /> : <div className="map-panel map-placeholder">{error ? "Map unavailable" : "Loading map…"}</div>}
        {selectedId && <section className="detail" aria-label="Church details">
          <button className="close-detail" onClick={() => setSelectedId(null)} aria-label="Close church details">×</button>
          {detailError ? <p role="alert">{detailError}</p> : detail ? <>
            <h2>{detail.name}</h2><p>{detail.denomination}</p><p>{address(detail)}</p>
            {detail.description && <p>{detail.description}</p>}
            <div className="detail-links">{detail.website_url && <a href={detail.website_url} target="_blank" rel="noopener noreferrer">Visit website ↗</a>}{hasCoordinates(detail) && <a href={`https://www.google.com/maps/dir/?api=1&destination=${detail.latitude},${detail.longitude}`} target="_blank" rel="noopener noreferrer">Directions ↗</a>}{detail.phone && <a href={`tel:${detail.phone}`}>{detail.phone}</a>}{detail.email && <a href={`mailto:${detail.email}`}>{detail.email}</a>}</div>
            <h3>Service times</h3>{detail.service_times.length ? <><ul>{detail.service_times.map((service) => <li key={service.id}>{days[service.day_of_week]} at {service.start_time.slice(0, 5)}{service.language && ` · ${service.language}`}<small>{[service.label, service.notes].filter(Boolean).join(" — ")}</small></li>)}</ul><small>Local time · {detail.timezone}</small></> : <p>Service times aren't listed. Check with the church before visiting.</p>}
            {detail.source_url && <a className="source-link" href={detail.source_url} target="_blank" rel="noopener noreferrer">View source ↗</a>}
          </> : <p role="status">Loading church details…</p>}
        </section>}
      </div>
      <footer>Coordinate data © <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap contributors</a></footer>
    </main>
  );
}
