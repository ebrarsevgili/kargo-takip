// Public tracking page: look up a shipment by tracking number. No sign-in needed.

import { h, api, ApiError, statusChip, routeStrip, timeline, formatDate, STATUS_LABELS } from "./ui.js";
import { showRouteMap } from "./map.js";

const form = document.getElementById("track-form");
const input = document.getElementById("tracking-number");
const button = document.getElementById("track-button");
const errorBox = document.getElementById("tracking-error");
const result = document.getElementById("result");

let currentMap = null;

function showInputError(message) {
  errorBox.textContent = message ?? "";
  errorBox.classList.toggle("hidden", !message);
  input.setAttribute("aria-invalid", message ? "true" : "false");
}

function clearResult() {
  currentMap?.destroy();
  currentMap = null;
  result.replaceChildren();
}

function renderNotFound(message) {
  result.replaceChildren(
    h("div", { class: "panel panel-pad" },
      h("h2", null, "Kargo bulunamadı"),
      h("p", { class: "muted" }, message),
      h("p", { class: "muted" }, "Takip numarası KT ile başlar ve ardından 10 rakam gelir. Örnek: KT1234567890")));
}

function render(data) {
  document.title = `${data.trackingNumber} - Kargo Takip`;
  const last = data.events.at(-1);

  const mapHost = h("div");
  const summary = h("div", { class: "panel panel-pad" },
    h("div", { class: "result-head" },
      h("div", { class: "tn" }, data.trackingNumber),
      statusChip(data.status)),
    routeStrip(data.originCity, data.destinationCity, data.status, { large: true }),
    data.status === "Cancelled" ? h("div", { class: "notice error" }, "Bu gönderi iptal edildi.") : null,
    h("dl", { class: "facts" },
      h("dt", null, "Alıcı"), h("dd", null, data.receiverMasked),
      h("dt", null, "Kayıt tarihi"), h("dd", null, formatDate(data.createdAt)),
      last ? [h("dt", null, "Son hareket"), h("dd", null, `${STATUS_LABELS[last.status] ?? last.status} - ${last.location}`)] : null));

  const mapPanel = h("div", { class: "panel panel-pad" }, h("h2", null, "Güzergâh"), mapHost);
  const timelinePanel = h("div", { class: "panel panel-pad" }, h("h2", null, "Hareketler"), timeline(data.events));

  result.replaceChildren(summary, h("div", { class: "result-grid" }, mapPanel, timelinePanel));

  showRouteMap(mapHost, { origin: data.originCity, destination: data.destinationCity, events: data.events })
    .then(handle => { currentMap = handle; });
}

async function lookup(number) {
  clearResult();
  button.disabled = true;
  try {
    render(await api(`/api/track/${encodeURIComponent(number)}`));
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    if (error.status === 404) renderNotFound("Bu numarayla kayıtlı bir gönderi yok.");
    else showInputError(error.message);
  } finally {
    button.disabled = false;
  }
}

form.addEventListener("submit", event => {
  event.preventDefault();
  const number = input.value.trim().toUpperCase();
  input.value = number;
  showInputError(null);
  if (!number) {
    clearResult();
    return showInputError("Takip numaranı yazmalısın.");
  }

  history.replaceState(null, "", `?no=${encodeURIComponent(number)}`);   // so the link can be shared
  lookup(number);
});

const initial = new URLSearchParams(location.search).get("no");
if (initial) {
  input.value = initial.trim().toUpperCase().slice(0, 12);
  lookup(input.value);
}
