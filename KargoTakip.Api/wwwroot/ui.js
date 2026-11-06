// Shared building blocks for every page: DOM helper, API client, dialogs, toasts and small components.
// Text from the server is always added with textContent / text nodes, never innerHTML (protects against XSS).

export const STATUS_LABELS = {
  Created: "Kayıt oluşturuldu",
  PickedUp: "Gönderenden alındı",
  InTransit: "Transfer halinde",
  OutForDelivery: "Dağıtıma çıktı",
  Delivered: "Teslim edildi",
  Cancelled: "İptal edildi"
};

/** Normal order of a shipment's life (cancelling is outside this flow). */
export const STATUS_FLOW = ["Created", "PickedUp", "InTransit", "OutForDelivery", "Delivered"];

// How far along the route strip the marker sits for each status.
const STATUS_PROGRESS = { Created: 0, PickedUp: 24, InTransit: 55, OutForDelivery: 82, Delivered: 100, Cancelled: 0 };

export const ROLE_LABELS = { Admin: "Yönetici", Courier: "Kurye", Customer: "Müşteri" };

export const VEHICLE_TYPES = { motorbike: "Motosiklet", car: "Otomobil", van: "Panelvan", truck: "Kamyon" };

// ---------------------------------------------------------------------------
// DOM
// ---------------------------------------------------------------------------

/** h("div", { class: "x", onclick: fn }, child, "text", [more, children]) */
export function h(tag, props, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(props || {})) {
    if (value === false || value == null) continue;
    if (key === "class") node.className = value;
    else if (key.startsWith("on") && typeof value === "function") node.addEventListener(key.slice(2), value);
    else node.setAttribute(key, value === true ? "" : value);
  }
  for (const child of children.flat(Infinity)) {
    if (child == null || child === false) continue;
    node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return node;
}

const SVG_NS = "http://www.w3.org/2000/svg";

const ICONS = {
  grid: ["M3 3h7v7H3z", "M14 3h7v7h-7z", "M3 14h7v7H3z", "M14 14h7v7h-7z"],
  box: ["M21 8l-9-5-9 5v8l9 5 9-5z", "M3 8l9 5 9-5", "M12 13v8"],
  users: ["M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2", "M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z", "M22 21v-2a4 4 0 0 0-3-3.87", "M16 3.13a4 4 0 0 1 0 7.75"],
  truck: ["M1 4h14v12H1z", "M15 8h4l4 4v4h-8z", "M6 21a2 2 0 1 0 0-4 2 2 0 0 0 0 4z", "M19 21a2 2 0 1 0 0-4 2 2 0 0 0 0 4z"],
  plus: ["M12 5v14", "M5 12h14"],
  search: ["M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16z", "M21 21l-4.3-4.3"],
  x: ["M18 6L6 18", "M6 6l12 12"],
  printer: ["M6 9V2h12v7", "M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2", "M6 14h12v8H6z"],
  pencil: ["M12 20h9", "M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"],
  trash: ["M3 6h18", "M8 6V4h8v2", "M19 6l-1 14H6L5 6", "M10 11v6", "M14 11v6"],
  logout: ["M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4", "M16 17l5-5-5-5", "M21 12H9"],
  link: ["M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7", "M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7"],
  chevronLeft: ["M15 18l-6-6 6-6"],
  chevronRight: ["M9 18l6-6-6-6"],
  check: ["M20 6L9 17l-5-5"]
};

export function icon(name, size = 18) {
  const svg = document.createElementNS(SVG_NS, "svg");
  for (const [k, v] of Object.entries({
    viewBox: "0 0 24 24", width: size, height: size, fill: "none", stroke: "currentColor",
    "stroke-width": 2, "stroke-linecap": "round", "stroke-linejoin": "round", "aria-hidden": "true", focusable: "false"
  })) svg.setAttribute(k, v);
  for (const d of ICONS[name] ?? []) {
    const path = document.createElementNS(SVG_NS, "path");
    path.setAttribute("d", d);
    svg.append(path);
  }
  return svg;
}

export function debounce(fn, ms = 250) {
  let timer;
  return (...args) => {
    clearTimeout(timer);
    timer = setTimeout(() => fn(...args), ms);
  };
}

// ---------------------------------------------------------------------------
// Formatting and small components
// ---------------------------------------------------------------------------

export function formatDate(iso) {
  return new Date(iso).toLocaleString("tr-TR", { dateStyle: "medium", timeStyle: "short" });
}

export function formatDay(iso) {
  return new Date(iso).toLocaleDateString("tr-TR", { dateStyle: "medium" });
}

export function formatKg(value) {
  return `${Number(value).toLocaleString("tr-TR", { maximumFractionDigits: 2 })} kg`;
}

/** Same letter folding as the server's TextSearch.Fold: "İstanbul", "istanbul" and "ISTANBUL" are equal. */
export function fold(text) {
  const map = { ç: "c", ğ: "g", ı: "i", İ: "i", I: "i", î: "i", ö: "o", ş: "s", ü: "u", û: "u", â: "a" };
  return [...String(text ?? "")].map(ch => map[ch] ?? map[ch.toLowerCase()] ?? ch.toLowerCase()).join("");
}

export function statusChip(status) {
  return h("span", { class: `chip ${status}` }, STATUS_LABELS[status] ?? status);
}

/** Origin, a marker showing how far the shipment has got, and the destination. */
export function routeStrip(origin, destination, status, { large = false } = {}) {
  const cls = ["route", large && "large", status === "Delivered" && "done", status === "Cancelled" && "cancelled"]
    .filter(Boolean).join(" ");

  const strip = h("div", { class: cls, role: "img", "aria-label": `${origin} - ${destination}, ${STATUS_LABELS[status] ?? status}` },
    h("span", { class: "city", title: origin }, origin),
    h("span", { class: "line" }, h("i", { class: "mark" }), h("i", { class: "end-dot" })),
    h("span", { class: "city to", title: destination }, destination));
  strip.style.setProperty("--progress", `${STATUS_PROGRESS[status] ?? 0}%`);
  return strip;
}

/** Newest first, as the server sends them oldest first. */
export function timeline(events) {
  return h("ul", { class: "timeline" },
    [...events].reverse().map(e =>
      h("li", null,
        h("div", { class: "what" }, STATUS_LABELS[e.status] ?? e.status, " - ", e.location),
        h("div", { class: "when" }, formatDate(e.timestamp)),
        e.note ? h("div", { class: "note" }, e.note) : null)));
}

export function emptyState(title, text, action) {
  return h("div", { class: "empty" }, h("strong", null, title), text ? h("p", null, text) : null, action ?? null);
}

// ---------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------

export class ApiError extends Error {
  constructor(status, message, fields = null) {
    super(message);
    this.status = status;
    this.fields = fields;   // { fieldName: ["message", ...] } from validation problems
  }
}

let onUnauthorized = null;

/** Called when a request comes back 401 (the session ended). Login requests are exempt. */
export function setUnauthorizedHandler(fn) {
  onUnauthorized = fn;
}

function problemMessage(data, status) {
  if (data && data.errors) return Object.values(data.errors).flat().join(" ");
  if (data && (data.detail || data.title)) return data.detail || data.title;
  return `Beklenmeyen bir hata oluştu (${status}).`;
}

export async function api(path, { method = "GET", body } = {}) {
  const headers = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";

  let response;
  try {
    response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch {
    throw new ApiError(0, "Sunucuya ulaşılamadı. Bağlantını kontrol et.");
  }

  if (response.status === 429) throw new ApiError(429, "Çok fazla istek gönderdin. Bir dakika sonra tekrar dene.");

  let data = null;
  const text = await response.text();
  if (text) {
    try { data = JSON.parse(text); } catch { /* body is not JSON: ignore */ }
  }

  if (response.status === 401 && onUnauthorized && !path.startsWith("/api/auth/login")) onUnauthorized();
  if (!response.ok) throw new ApiError(response.status, problemMessage(data, response.status), data?.errors ?? null);
  return data;
}

// ---------------------------------------------------------------------------
// Toasts
// ---------------------------------------------------------------------------

export function toast(message, kind = "ok") {
  let region = document.querySelector(".toast-region");
  if (!region) {
    region = h("div", { class: "toast-region", role: "status", "aria-live": "polite" });
    document.body.append(region);
  }
  const node = h("div", { class: `toast ${kind === "error" ? "error" : ""}` }, message);
  region.append(node);
  setTimeout(() => node.remove(), kind === "error" ? 6000 : 3500);
}

// ---------------------------------------------------------------------------
// Dialogs
// ---------------------------------------------------------------------------

/**
 * Builds a native <dialog> (focus trap and Esc come from the browser).
 * Returns { dialog, close } and removes the element again when it closes.
 */
export function openDialog({ title, body, footer, className = "modal", onClose }) {
  const dialog = h("dialog", { class: className, "aria-label": title });
  const close = () => dialog.close();

  dialog.append(
    h("div", { class: "modal-head" },
      h("h2", null, title),
      h("button", { type: "button", class: "btn btn-quiet btn-icon", "aria-label": "Kapat", onclick: close }, icon("x"))),
    body,
    ...(footer ? [footer] : []));

  dialog.addEventListener("close", () => {
    dialog.remove();
    onClose?.();
  });
  // Clicking the dimmed area closes the dialog (only when the click lands on the dialog element itself).
  dialog.addEventListener("click", event => { if (event.target === dialog) close(); });

  document.body.append(dialog);
  dialog.showModal();
  return { dialog, close };
}

/** Yes/no question. Resolves to true only when the confirm button is pressed. */
export function confirmDialog({ title, message, confirmLabel = "Onayla", danger = false }) {
  return new Promise(resolve => {
    let answer = false;
    const confirm = h("button", { type: "button", class: `btn ${danger ? "btn-danger" : "btn-primary"}`, autofocus: true,
      onclick: () => { answer = true; handle.close(); } }, confirmLabel);
    const handle = openDialog({
      title,
      body: h("div", { class: "modal-body" }, h("p", null, message)),
      footer: h("div", { class: "modal-foot" },
        h("button", { type: "button", class: "btn", onclick: () => handle.close() }, "Vazgeç"), confirm),
      onClose: () => resolve(answer)
    });
  });
}

let fieldCounter = 0;

function buildField(def) {
  const id = `f${++fieldCounter}-${def.name}`;
  const common = { id, name: def.name, "aria-describedby": `${id}-msg` };
  let control;

  if (def.options) {
    control = h("select", { ...common },
      def.placeholder !== undefined ? h("option", { value: "" }, def.placeholder) : null,
      def.options.map(o => h("option", { value: o.value }, o.label)));
  } else if (def.type === "textarea") {
    control = h("textarea", { ...common, maxlength: def.maxlength, rows: def.rows ?? 3 });
  } else {
    control = h("input", {
      ...common,
      type: def.type ?? "text",
      maxlength: def.maxlength,
      list: def.list,
      autocomplete: def.autocomplete ?? "off",
      inputmode: def.inputmode,
      placeholder: def.placeholder,
      autocapitalize: def.autocapitalize,
      spellcheck: def.type === "password" ? "false" : undefined
    });
  }

  const message = h("div", { id: `${id}-msg`, class: "field-error hidden" });
  const wrapper = h("div", { class: `field ${def.span2 ? "span-2" : ""}` },
    h("label", { for: id }, def.label),
    control,
    def.hint ? h("div", { class: "hint" }, def.hint) : null,
    message);

  return { def, control, message, wrapper };
}

function readValue(field) {
  const raw = field.control.value.trim();
  if (raw === "") return null;
  if (field.def.number) return Number(raw.replace(",", "."));
  return field.def.type === "password" ? field.control.value : raw;   // keep spaces inside passwords
}

/**
 * A form in a dialog. fields: [{ name, label, type, options, required, number, hint, span2, ... }].
 * onSubmit(values) does the API call; if it throws an ApiError the message (or the field errors) are shown
 * inside the form and the dialog stays open. Resolves with onSubmit's result, or undefined when cancelled.
 */
export function formDialog({ title, fields, initial = {}, submitLabel = "Kaydet", intro, onSubmit }) {
  return new Promise(resolve => {
    let result;
    const built = fields.map(buildField);
    built.forEach((f, index) => {
      const start = initial[f.def.name];
      if (start != null) f.control.value = String(start);
      if (index === 0) f.control.setAttribute("autofocus", "");
    });

    const errorBox = h("div", { class: "notice error hidden", role: "alert" });
    const submit = h("button", { type: "submit", class: "btn btn-primary" }, submitLabel);
    const cancel = h("button", { type: "button", class: "btn", onclick: () => handle.close() }, "Vazgeç");

    const form = h("form", { novalidate: true, class: "modal-form" },
      h("div", { class: "modal-body" }, intro ? h("p", { class: "muted" }, intro) : null, errorBox,
        h("div", { class: "form-grid" }, built.map(f => f.wrapper))),
      h("div", { class: "modal-foot" }, cancel, submit));

    const showFieldError = (field, text) => {
      field.message.textContent = text ?? "";
      field.message.classList.toggle("hidden", !text);
      field.control.setAttribute("aria-invalid", text ? "true" : "false");
    };

    form.addEventListener("submit", async event => {
      event.preventDefault();
      errorBox.classList.add("hidden");

      let valid = true;
      const values = {};
      for (const f of built) {
        const value = readValue(f);
        values[f.def.name] = value;
        let problem = null;
        if (f.def.required && value == null) problem = "Bu alan zorunlu.";
        else if (f.def.number && value != null && Number.isNaN(value)) problem = "Geçerli bir sayı gir.";
        showFieldError(f, problem);
        valid &&= !problem;
      }
      if (!valid) return built.find(f => f.control.getAttribute("aria-invalid") === "true")?.control.focus();

      submit.disabled = cancel.disabled = true;
      try {
        result = (await onSubmit(values)) ?? true;
        handle.close();
      } catch (error) {
        if (!(error instanceof ApiError)) throw error;
        let shown = false;
        for (const [key, messages] of Object.entries(error.fields ?? {})) {
          const f = built.find(x => x.def.name.toLowerCase() === key.toLowerCase());
          if (f) { showFieldError(f, [messages].flat().join(" ")); shown = true; }
        }
        if (!shown) {
          errorBox.textContent = error.message;
          errorBox.classList.remove("hidden");
        }
      } finally {
        submit.disabled = cancel.disabled = false;
      }
    });

    const handle = openDialog({ title, body: form, onClose: () => resolve(result) });
  });
}
