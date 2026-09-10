import { useEffect, useRef, useState } from "react";
import { importLibrary, setOptions } from "@googlemaps/js-api-loader";
import { hasCoordinates, type Church, type MapConfig } from "./types";

type Props = {
  config: MapConfig;
  churches: Church[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onSearchArea: (bbox: string) => void;
  onStatus: (status: "ready" | "error") => void;
};

export function ChurchMap({ config, churches, selectedId, onSelect, onSearchArea, onStatus }: Props) {
  const container = useRef<HTMLDivElement>(null);
  const map = useRef<google.maps.Map | null>(null);
  const markers = useRef(new Map<string, google.maps.marker.AdvancedMarkerElement>());
  const initialFit = useRef(false);
  const onSelectRef = useRef(onSelect);
  onSelectRef.current = onSelect;
  const [ready, setReady] = useState(false);
  const [error, setError] = useState("");
  const [areaError, setAreaError] = useState("");

  useEffect(() => {
    let cancelled = false;
    let failed = false;
    setReady(false);
    setError("");
    initialFit.current = false;
    const fail = () => {
      if (cancelled) return;
      failed = true;
      setError("The map couldn't load. You can still browse churches in the list.");
      onStatus("error");
    };
    if (!config.google_maps_api_key) {
      fail();
      return;
    }
    const global = window as Window & { gm_authFailure?: () => void };
    const previousAuthFailure = global.gm_authFailure;
    global.gm_authFailure = fail;
    setOptions({ key: config.google_maps_api_key, v: "weekly" });
    Promise.all([importLibrary("maps"), importLibrary("marker")])
      .then(([mapsLibrary]) => {
        if (cancelled || !container.current) return;
        const { Map: GoogleMap } = mapsLibrary as google.maps.MapsLibrary;
        map.current = new GoogleMap(container.current, {
          center: { lat: 43.66, lng: -79.38 },
          zoom: 12,
          mapId: config.google_maps_map_id || "DEMO_MAP_ID",
          mapTypeControl: false,
          streetViewControl: false,
          gestureHandling: "cooperative",
        });
        setReady(true);
        google.maps.event.addListenerOnce(map.current, "idle", () => {
          if (!cancelled && !failed) onStatus("ready");
        });
      })
      .catch(fail);
    return () => {
      cancelled = true;
      global.gm_authFailure = previousAuthFailure;
      if (map.current) google.maps.event.clearInstanceListeners(map.current);
      markers.current.forEach((marker) => { marker.map = null; });
      markers.current.clear();
      map.current = null;
    };
  }, [config, onStatus]);

  useEffect(() => {
    if (!ready || !map.current) return;
    markers.current.forEach((marker) => {
      google.maps.event.clearInstanceListeners(marker);
      marker.map = null;
    });
    markers.current.clear();
    const bounds = new google.maps.LatLngBounds();
    for (const church of churches.filter(hasCoordinates)) {
      const position = { lat: church.latitude!, lng: church.longitude! };
      bounds.extend(position);
      const marker = new google.maps.marker.AdvancedMarkerElement({
        position, map: map.current, title: church.name,
      });
      marker.addListener("click", () => onSelectRef.current(church.id));
      markers.current.set(church.id, marker);
    }
    if (!initialFit.current && !bounds.isEmpty()) {
      map.current.fitBounds(bounds, 50);
      initialFit.current = true;
    }
  }, [churches, ready]);

  useEffect(() => {
    if (!ready) return;
    for (const [id, marker] of markers.current) {
      const selected = id === selectedId;
      marker.zIndex = selected ? 100 : 1;
      const pin = new google.maps.marker.PinElement({
        background: selected ? "#1e40af" : "#d93025",
        borderColor: selected ? "#172554" : "#9f2017",
        glyphColor: "white",
        scale: selected ? 1.3 : 1,
      });
      marker.replaceChildren(pin);
    }
    const church = churches.find((item) => item.id === selectedId);
    if (church && hasCoordinates(church)) {
      map.current?.panTo({ lat: church.latitude!, lng: church.longitude! });
    }
  }, [selectedId, churches, ready]);

  function searchArea() {
    const bounds = map.current?.getBounds();
    if (!bounds) return;
    const { west, south, east, north } = bounds.toJSON();
    if (west > east) {
      setAreaError("This area crosses the date line. Zoom in or move the map before searching.");
    } else {
      setAreaError("");
      onSearchArea([west, south, east, north].join(","));
    }
  }

  return (
    <section className="map-panel" aria-label="Church locations">
      <div ref={container} className="map-canvas" />
      {ready && !error && <button className="search-area" onClick={searchArea}>Search this area</button>}
      {areaError && <p className="area-error" role="status">{areaError}</p>}
      {(!ready || error) && <div className="map-message" role="status">{error || "Loading map…"}</div>}
    </section>
  );
}
