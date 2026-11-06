// The 81 provinces of Turkey with approximate coordinates of their centres (used to draw route maps),
// plus a lookup that understands how people actually type a city: "istanbul", "İZMİR", "Kocaeli Aktarma Merkezi".

import { fold, h } from "./ui.js";

// [name, latitude, longitude, ...extra names people use]
const RAW = [
  ["Adana", 37.00, 35.32], ["Adıyaman", 37.76, 38.28], ["Afyonkarahisar", 38.76, 30.54, "Afyon"],
  ["Ağrı", 39.72, 43.05], ["Aksaray", 38.37, 34.03], ["Amasya", 40.65, 35.83], ["Ankara", 39.93, 32.86],
  ["Antalya", 36.90, 30.69], ["Ardahan", 41.11, 42.70], ["Artvin", 41.18, 41.82], ["Aydın", 37.85, 27.85],
  ["Balıkesir", 39.65, 27.89], ["Bartın", 41.64, 32.34], ["Batman", 37.89, 41.13], ["Bayburt", 40.26, 40.23],
  ["Bilecik", 40.14, 29.98], ["Bingöl", 38.88, 40.50], ["Bitlis", 38.40, 42.11], ["Bolu", 40.74, 31.61],
  ["Burdur", 37.72, 30.29], ["Bursa", 40.19, 29.06], ["Çanakkale", 40.15, 26.41], ["Çankırı", 40.60, 33.62],
  ["Çorum", 40.55, 34.95], ["Denizli", 37.78, 29.09], ["Diyarbakır", 37.91, 40.24], ["Düzce", 40.84, 31.16],
  ["Edirne", 41.68, 26.56], ["Elazığ", 38.67, 39.22], ["Erzincan", 39.75, 39.49], ["Erzurum", 39.90, 41.27],
  ["Eskişehir", 39.78, 30.52], ["Gaziantep", 37.07, 37.38, "Antep"], ["Giresun", 40.91, 38.39],
  ["Gümüşhane", 40.46, 39.48], ["Hakkâri", 37.58, 43.74, "Hakkari"], ["Hatay", 36.20, 36.16, "Antakya"],
  ["Iğdır", 39.92, 44.05], ["Isparta", 37.76, 30.55], ["İstanbul", 41.01, 28.98],
  ["İzmir", 38.42, 27.14], ["Kahramanmaraş", 37.58, 36.94, "Maraş"], ["Karabük", 41.20, 32.62],
  ["Karaman", 37.18, 33.23], ["Kars", 40.60, 43.10], ["Kastamonu", 41.38, 33.78], ["Kayseri", 38.73, 35.49],
  ["Kilis", 36.72, 37.12], ["Kırıkkale", 39.85, 33.51], ["Kırklareli", 41.74, 27.23], ["Kırşehir", 39.15, 34.17],
  ["Kocaeli", 40.77, 29.92, "İzmit"], ["Konya", 37.87, 32.48], ["Kütahya", 39.42, 29.98], ["Malatya", 38.35, 38.31],
  ["Manisa", 38.61, 27.43], ["Mardin", 37.31, 40.74], ["Mersin", 36.81, 34.64, "İçel"], ["Muğla", 37.22, 28.36],
  ["Muş", 38.74, 41.49], ["Nevşehir", 38.62, 34.71], ["Niğde", 37.97, 34.68], ["Ordu", 40.98, 37.88],
  ["Osmaniye", 37.07, 36.25], ["Rize", 41.02, 40.52], ["Sakarya", 40.78, 30.40, "Adapazarı"], ["Samsun", 41.29, 36.33],
  ["Şanlıurfa", 37.16, 38.79, "Urfa"], ["Siirt", 37.93, 41.94], ["Sinop", 42.03, 35.15], ["Şırnak", 37.52, 42.46],
  ["Sivas", 39.75, 37.02], ["Tekirdağ", 40.98, 27.51], ["Tokat", 40.31, 36.55], ["Trabzon", 41.00, 39.72],
  ["Tunceli", 39.11, 39.55], ["Uşak", 38.68, 29.41], ["Van", 38.49, 43.38], ["Yalova", 40.65, 29.27],
  ["Yozgat", 39.82, 34.81], ["Zonguldak", 41.46, 31.80]
];

export const CITIES = RAW.map(([name, lat, lng, ...aliases]) => ({
  name, lat, lng,
  keys: [name, ...aliases].map(fold)
}));

/** For <datalist>: the official names, in alphabetical order for Turkish. */
export const CITY_NAMES = CITIES.map(c => c.name).sort((a, b) => a.localeCompare(b, "tr"));

/**
 * Finds the city mentioned in a free text. "Ankara Hub" -> Ankara. When several names appear, the longest wins,
 * and a name only counts when it starts a word, so "Van" is not found inside "Advance".
 */
export function findCity(text) {
  const haystack = ` ${fold(text).replace(/[^a-z0-9]+/g, " ")} `;
  let best = null;
  let bestLength = 0;
  for (const city of CITIES) {
    for (const key of city.keys) {
      if (key.length > bestLength && haystack.includes(` ${key}`)) {
        best = city;
        bestLength = key.length;
      }
    }
  }
  return best;
}

/** Adds <datalist id="city-list"> to the page once, so any input with list="city-list" suggests the provinces. */
export function ensureCityList() {
  if (document.getElementById("city-list")) return;
  document.body.append(h("datalist", { id: "city-list" }, CITY_NAMES.map(name => h("option", { value: name }))));
}
