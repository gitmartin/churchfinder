export type Church = {
  id: string;
  name: string;
  denomination: string | null;
  address_line1: string | null;
  city: string | null;
  region: string | null;
  postal_code: string | null;
  country_code: string | null;
  latitude: number | null;
  longitude: number | null;
};

export type ChurchDetail = Church & {
  description: string | null;
  timezone: string | null;
  website_url: string | null;
  phone: string | null;
  email: string | null;
  source_url: string | null;
  service_times: {
    id: string;
    day_of_week: number;
    start_time: string;
    language: string | null;
    label: string | null;
    notes: string | null;
  }[];
};

export type Page = { items: Church[]; total: number; limit: number; offset: number };
export type Filters = { cities: string[]; denominations: string[]; languages: string[] };
export type MapConfig = { google_maps_api_key: string; google_maps_map_id: string };

export async function getJSON<T>(url: string, signal?: AbortSignal): Promise<T> {
  const timeout = AbortSignal.timeout(15000);
  const response = await fetch(url, { signal: signal ? AbortSignal.any([signal, timeout]) : timeout });
  if (!response.ok) throw new Error("Couldn't load the church data. Please try again.");
  return response.json() as Promise<T>;
}

export function address(church: Church): string {
  return [church.address_line1, church.city, church.region, church.postal_code].filter(Boolean).join(", ");
}

export function hasCoordinates(church: Church): boolean {
  return church.latitude !== null && church.longitude !== null;
}
