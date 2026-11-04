"use strict";

// Small helpers shared between pages. Every piece of text is added through textContent / text nodes;
// data coming from the server is never passed to innerHTML (protects against XSS).

const STATUS_LABELS = {
  Created: "Kayıt oluşturuldu",
  PickedUp: "Gönderenden alındı",
  InTransit: "Transfer halinde",
  OutForDelivery: "Dağıtıma çıktı",
  Delivered: "Teslim edildi",
  Cancelled: "İptal edildi"
};

// Order of the normal flow (cancelling is outside this flow).
const STATUS_FLOW = ["Created", "PickedUp", "InTransit", "OutForDelivery", "Delivered"];

function h(tag, props, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(props || {})) {
    if (value === false || value == null) continue;
    if (key === "class") node.className = value;
    else if (key.startsWith("on") && typeof value === "function") node.addEventListener(key.slice(2), value);
    else node.setAttribute(key, value === true ? "" : value);
  }
  for (const child of children.flat()) {
    if (child == null || child === false) continue;
    node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return node;
}

function statusBadge(status) {
  return h("span", { class: `badge ${status}` }, STATUS_LABELS[status] ?? status);
}

function formatDate(iso) {
  return new Date(iso).toLocaleString("tr-TR", { dateStyle: "medium", timeStyle: "short" });
}

class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

function problemMessage(data, status) {
  if (data && data.errors) return Object.values(data.errors).flat().join(" ");
  if (data && (data.detail || data.title)) return data.detail || data.title;
  return `Beklenmeyen bir hata oluştu (${status}).`;
}

async function api(path, { method = "GET", body } = {}) {
  const headers = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";

  let response;
  try {
    response = await fetch(path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body)
    });
  } catch {
    throw new ApiError(0, "Sunucuya ulaşılamadı. Bağlantını kontrol et.");
  }

  if (response.status === 429) {
    throw new ApiError(429, "Çok fazla sorgu yaptın. Bir dakika sonra tekrar dene.");
  }

  let data = null;
  const text = await response.text();
  if (text) {
    try { data = JSON.parse(text); } catch { /* body is not JSON: ignore */ }
  }

  if (!response.ok) throw new ApiError(response.status, problemMessage(data, response.status));
  return data;
}
