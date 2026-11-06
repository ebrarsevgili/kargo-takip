// Route map: origin, destination and the places a shipment has been seen, drawn with Leaflet
// (vendored under /vendor/leaflet; only the map tiles come from OpenStreetMap).

import { h } from "./ui.js";
import { findCity } from "./cities.js";

let leafletReady;

function loadLeaflet() {
  leafletReady ??= new Promise((resolve, reject) => {
    document.head.append(h("link", { rel: "stylesheet", href: "vendor/leaflet/leaflet.css" }));
    const script = h("script", { src: "vendor/leaflet/leaflet.js" });
    script.onload = () => resolve(window.L);
    script.onerror = () => { leafletReady = undefined; reject(new Error("Harita kütüphanesi yüklenemedi.")); };
    document.head.append(script);
  });
  return leafletReady;
}

const INK = "#151c28";
const TAPE = "#ffc400";

/**
 * Draws the route inside `container` (which must already be in the page).
 * events: [{ location }] oldest first. Locations are matched to provinces by name; unknown ones are skipped.
 * Returns { destroy() }.
 */
export async function showRouteMap(container, { origin, destination, events = [] }) {
  const from = findCity(origin);
  const to = findCity(destination);

  if (!from && !to) {
    container.replaceChildren(h("p", { class: "map-note" }, "Bu şehirler haritada gösterilemedi."));
    return { destroy() {} };
  }

  let L;
  try {
    L = await loadLeaflet();
  } catch (error) {
    container.replaceChildren(h("p", { class: "map-note" }, error.message));
    return { destroy() {} };
  }

  const mapNode = h("div", { class: "map", role: "img", "aria-label": `${origin} - ${destination} güzergâhı haritası` });
  container.replaceChildren(mapNode);

  const map = L.map(mapNode, { scrollWheelZoom: false, zoomControl: true, attributionControl: true });
  L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
    maxZoom: 12,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a>'
  }).addTo(map);

  const point = c => [c.lat, c.lng];
  const bounds = [];

  // Planned route: origin to destination.
  if (from && to && from !== to) {
    L.polyline([point(from), point(to)], { color: INK, weight: 2, opacity: 0.55, dashArray: "6 8" }).addTo(map);
  }

  // Travelled route: through every distinct place the shipment was seen, in order.
  const seen = [];
  for (const event of events) {
    const city = findCity(event.location);
    if (city && seen.at(-1) !== city) seen.push(city);
  }
  const travelled = [from, ...seen].filter(Boolean).filter((c, i, all) => all[i - 1] !== c);
  if (travelled.length > 1) {
    L.polyline(travelled.map(point), { color: INK, weight: 4 }).addTo(map);
  }

  for (const city of seen) {
    if (city === from || city === to) continue;
    L.circleMarker(point(city), { radius: 6, color: INK, weight: 2, fillColor: TAPE, fillOpacity: 1 })
      .bindTooltip(h("span", null, city.name), { direction: "top", className: "city-tip" })
      .addTo(map);
    bounds.push(point(city));
  }

  const endpoint = (city, filled) => {
    L.circleMarker(point(city), { radius: 8, color: INK, weight: 3, fillColor: filled ? INK : "#fff", fillOpacity: 1 })
      .bindTooltip(h("span", null, city.name), { permanent: true, direction: "top", className: "city-tip", offset: [0, -6] })
      .addTo(map);
    bounds.push(point(city));
  };
  if (from) endpoint(from, true);
  if (to && to !== from) endpoint(to, false);

  if (bounds.length === 1) map.setView(bounds[0], 8);
  else map.fitBounds(bounds, { padding: [48, 48], maxZoom: 8 });

  // The container may still be animating into view (drawer) or hidden: re-measure shortly after.
  // The timers are cancelled in destroy(), because measuring a map that was already removed throws.
  const timers = [60, 320].map(ms => setTimeout(() => map.invalidateSize(), ms));

  return {
    destroy() {
      timers.forEach(clearTimeout);
      map.remove();
    }
  };
}
