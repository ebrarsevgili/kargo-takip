// Printable shipping label (A6) with a QR code that opens the public tracking page.
// Needs a signed-in user who may see the shipment (admin, the owning customer, or the assigned courier).

import { h, icon, api, ApiError, formatDay, formatKg, setUnauthorizedHandler } from "./ui.js";
import qrcode from "./vendor/qrcode/qrcode.mjs";

const SVG_NS = "http://www.w3.org/2000/svg";
const page = document.getElementById("page");

setUnauthorizedHandler(() => location.replace("login.html"));

/** Draws a QR code as an <svg> built from DOM nodes (no innerHTML). */
function qrSvg(text) {
  const qr = qrcode(0, "M");
  qr.addData(text);
  qr.make();

  const count = qr.getModuleCount();
  const quiet = 2;   // empty border, in modules, so scanners find the code
  let d = "";
  for (let row = 0; row < count; row++) {
    for (let col = 0; col < count; col++) {
      if (qr.isDark(row, col)) d += `M${col + quiet} ${row + quiet}h1v1h-1z`;
    }
  }

  const svg = document.createElementNS(SVG_NS, "svg");
  svg.setAttribute("viewBox", `0 0 ${count + quiet * 2} ${count + quiet * 2}`);
  svg.setAttribute("shape-rendering", "crispEdges");
  svg.setAttribute("role", "img");
  svg.setAttribute("aria-label", "Takip sayfasına giden QR kod");

  const background = document.createElementNS(SVG_NS, "rect");
  for (const [k, v] of Object.entries({ width: count + quiet * 2, height: count + quiet * 2, fill: "#fff" })) background.setAttribute(k, v);
  const path = document.createElementNS(SVG_NS, "path");
  path.setAttribute("d", d);
  path.setAttribute("fill", "#000");
  svg.append(background, path);
  return svg;
}

function label(shipment) {
  const trackingUrl = `${location.origin}/?no=${shipment.trackingNumber}`;

  return h("article", { class: "label-sheet", "aria-label": `Kargo etiketi ${shipment.trackingNumber}` },
    h("section", { class: "label-top" },
      h("span", { class: "brand" }, h("span", { class: "brand-mark" }), "Kargo Takip"),
      h("span", { class: "label-weight" }, formatKg(shipment.weightKg))),

    h("section", null,
      h("div", { class: "label-tn" }, shipment.trackingNumber),
      h("div", null, formatDay(shipment.createdAt))),

    h("section", { class: "label-to" },
      h("div", { class: "label-party" },
        h("small", null, "Gönderen"),
        h("strong", null, shipment.senderName),
        h("span", { class: "city" }, shipment.originCity)),
      h("div", { class: "label-party" },
        h("small", null, "Alıcı"),
        h("strong", null, shipment.receiverName),
        shipment.receiverPhone ? h("span", null, shipment.receiverPhone) : null),
      h("div", { class: "city" }, shipment.destinationCity)),

    h("section", { class: "label-bottom" },
      h("div", { class: "qr" }, qrSvg(trackingUrl)),
      h("div", { class: "scan" }, "Kargonun nerede olduğunu görmek için QR kodu okut ya da takip numaranı ", h("strong", null, location.host), " adresine yaz.")));
}

async function main() {
  const number = new URLSearchParams(location.search).get("no")?.trim().toUpperCase();
  if (!number) {
    page.replaceChildren(h("div", { class: "notice error" }, "Takip numarası verilmedi."));
    return;
  }

  let shipment;
  try {
    shipment = await api(`/api/shipments/${encodeURIComponent(number)}`);
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    if (error.status === 401) return;   // the unauthorized handler is redirecting to the login page
    page.replaceChildren(h("div", { class: "notice error", role: "alert" },
      error.status === 404 ? "Bu numarayla erişebileceğin bir kargo bulunamadı." : error.message));
    return;
  }

  document.title = `Etiket ${shipment.trackingNumber}`;
  page.replaceChildren(
    h("div", { class: "label-tools no-print" },
      h("button", { type: "button", class: "btn btn-primary", onclick: () => window.print() }, icon("printer", 18), "Yazdır"),
      h("a", { class: "btn", href: "app.html#/kargolar" }, "Panele dön")),
    label(shipment));
}

main();
