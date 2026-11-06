// Shipment detail in a side panel: route, map, timeline and the actions the signed-in role is allowed to use.
//   Admin:    edit, delete, hand to a courier, add movements (including cancelling)
//   Courier:  add movements to shipments assigned to them
//   Customer: look at their own shipments

import {
  h, icon, api, ApiError, toast, statusChip, routeStrip, timeline, formatDate, formatKg,
  openDialog, formDialog, confirmDialog, STATUS_LABELS, STATUS_FLOW
} from "../ui.js";
import { showRouteMap } from "../map.js";
import { ensureCityList } from "../cities.js";

const rank = status => STATUS_FLOW.indexOf(status);

/** Opens the panel for one shipment. onChange() is called after anything was modified, so the list can refresh. */
export async function openShipment(trackingNumber, { me, onChange }) {
  ensureCityList();

  const isAdmin = me.role === "Admin";
  const host = h("div", { class: "drawer-body" }, h("p", { class: "muted" }, "Yükleniyor…"));
  let map = null;
  let shipment = null;
  let renderToken = 0;   // a map that finishes loading after a newer render (or after closing) must be thrown away

  const handle = openDialog({
    title: trackingNumber,
    body: host,
    className: "drawer",
    onClose: () => map?.destroy()
  });

  async function reload() {
    shipment = await api(`/api/shipments/${encodeURIComponent(trackingNumber)}`);
    await render();
  }

  async function render() {
    map?.destroy();
    map = null;
    const token = ++renderToken;

    const closed = shipment.status === "Delivered" || shipment.status === "Cancelled";
    const canMove = !closed && (isAdmin || me.role === "Courier");
    const couriers = isAdmin && !closed ? await api("/api/couriers") : [];

    const mapHost = h("div");

    // Native replaceChildren turns null into the text "null", so skipped sections are filtered out first.
    host.replaceChildren(...[
      h("div", { class: "drawer-title" },
        h("div", null, statusChip(shipment.status)),
        routeStrip(shipment.originCity, shipment.destinationCity, shipment.status, { large: true })),

      facts(),
      actions(closed),
      isAdmin && !closed ? assignment(couriers) : null,
      canMove ? movementForm() : null,

      h("div", { class: "block" }, h("h3", null, "Güzergâh"), mapHost),
      h("div", { class: "block" }, h("h3", null, "Hareketler"), timeline(shipment.events))
    ].filter(Boolean));

    showRouteMap(mapHost, { origin: shipment.originCity, destination: shipment.destinationCity, events: shipment.events })
      .then(m => { if (token === renderToken && handle.dialog.isConnected) map = m; else m.destroy(); });
  }

  function facts() {
    const rows = [
      ["Gönderen", shipment.senderName],
      ["Alıcı", shipment.receiverName],
      ["Telefon", shipment.receiverPhone ?? "-"],
      ["Ağırlık", formatKg(shipment.weightKg)],
      ["Kayıt tarihi", formatDate(shipment.createdAt)]
    ];
    if (isAdmin) rows.push(["Müşteri", shipment.ownerUsername ?? "-"]);
    if (me.role !== "Courier") rows.push(["Kurye", shipment.courierName ?? "Atanmadı"]);

    return h("dl", { class: "facts" }, rows.map(([k, v]) => [h("dt", null, k), h("dd", null, v)]));
  }

  function actions(closed) {
    return h("div", { class: "action-row" },
      me.role !== "Courier"
        ? h("a", { class: "btn btn-sm", href: `label.html?no=${encodeURIComponent(shipment.trackingNumber)}`, target: "_blank", rel: "noopener" }, icon("printer", 16), "Etiket yazdır")
        : null,
      h("button", { type: "button", class: "btn btn-sm", onclick: copyLink }, icon("link", 16), "Takip bağlantısını kopyala"),
      isAdmin && !closed ? h("button", { type: "button", class: "btn btn-sm", onclick: edit }, icon("pencil", 16), "Düzenle") : null,
      isAdmin ? h("button", { type: "button", class: "btn btn-sm btn-danger", onclick: remove }, icon("trash", 16), "Sil") : null);
  }

  async function copyLink() {
    const url = `${location.origin}/?no=${shipment.trackingNumber}`;
    try {
      await navigator.clipboard.writeText(url);
      toast("Takip bağlantısı kopyalandı.");
    } catch {
      toast(url);   // clipboard not available: show the link so it can be copied by hand
    }
  }

  async function edit() {
    const updated = await formDialog({
      title: "Kargoyu düzenle",
      fields: [
        { name: "senderName", label: "Gönderen", required: true, maxlength: 100 },
        { name: "receiverName", label: "Alıcı", required: true, maxlength: 100 },
        { name: "receiverPhone", label: "Alıcı telefonu", type: "tel", maxlength: 20, inputmode: "tel" },
        { name: "weightKg", label: "Ağırlık (kg)", required: true, number: true, inputmode: "decimal" },
        { name: "originCity", label: "Çıkış şehri", required: true, maxlength: 80, list: "city-list" },
        { name: "destinationCity", label: "Varış şehri", required: true, maxlength: 80, list: "city-list" }
      ],
      initial: { ...shipment, weightKg: String(shipment.weightKg).replace(".", ",") },
      onSubmit: values => api(`/api/shipments/${shipment.trackingNumber}`, { method: "PUT", body: values })
    });
    if (updated && updated !== true) {
      shipment = updated;
      await render();
      toast("Kargo güncellendi.");
      onChange();
    }
  }

  async function remove() {
    const ok = await confirmDialog({
      title: "Kargoyu sil",
      message: `${shipment.trackingNumber} numaralı kargo ve tüm hareketleri kalıcı olarak silinecek. Bu işlem geri alınamaz.`,
      confirmLabel: "Kargoyu sil",
      danger: true
    });
    if (!ok) return;

    try {
      await api(`/api/shipments/${shipment.trackingNumber}`, { method: "DELETE" });
    } catch (error) {
      return toast(error.message, "error");
    }
    handle.close();
    toast("Kargo silindi.");
    onChange();
  }

  function assignment(couriers) {
    const notice = h("div", { class: "notice error hidden", role: "alert" });
    const select = h("select", { id: "courier-select", "aria-label": "Kurye" },
      h("option", { value: "" }, "Atanmadı"),
      couriers.map(c => h("option", { value: c.id }, courierLabel(c))));
    select.value = shipment.courierId ?? "";

    const save = h("button", { type: "button", class: "btn", onclick: async () => {
      notice.classList.add("hidden");
      save.disabled = true;
      try {
        shipment = await api(`/api/shipments/${shipment.trackingNumber}/courier`, { method: "PUT", body: { courierId: select.value || null } });
        await render();
        toast(shipment.courierName ? `Kargo ${shipment.courierName} kuryesine atandı.` : "Kurye ataması kaldırıldı.");
        onChange();
      } catch (error) {
        if (!(error instanceof ApiError)) throw error;
        notice.textContent = error.message;
        notice.classList.remove("hidden");
        save.disabled = false;
      }
    } }, "Kaydet");

    return h("div", { class: "block" },
      h("h3", null, "Kurye"),
      couriers.length === 0
        ? h("p", { class: "muted" }, "Henüz kurye yok. Kuryeler sayfasından bir kurye ekle.")
        : h("div", { class: "inline-form" },
            h("div", { class: "field" }, h("label", { for: "courier-select" }, "Bu kargoyu taşıyacak kurye"), select),
            notice,
            h("div", null, save)));
  }

  function movementForm() {
    const current = Math.max(rank(shipment.status), 1);
    const options = [...STATUS_FLOW.slice(current), ...(isAdmin ? ["Cancelled"] : [])];
    const next = STATUS_FLOW[Math.min(rank(shipment.status) + 1, STATUS_FLOW.length - 1)];

    const notice = h("div", { class: "notice error hidden", role: "alert" });
    const status = h("select", { id: "move-status" }, options.map(s => h("option", { value: s }, STATUS_LABELS[s])));
    status.value = options.includes(next) ? next : options[0];
    const location = h("input", { id: "move-location", maxlength: 100, list: "city-list", autocomplete: "off", value: shipment.events.at(-1)?.location ?? "" });
    const note = h("input", { id: "move-note", maxlength: 300, autocomplete: "off", placeholder: "İsteğe bağlı" });
    const add = h("button", { type: "submit", class: "btn btn-primary" }, "Hareket ekle");

    const form = h("form", { class: "inline-form", novalidate: true },
      h("div", { class: "field" }, h("label", { for: "move-status" }, "Yeni durum"), status),
      h("div", { class: "field" }, h("label", { for: "move-location" }, "Konum"), location),
      h("div", { class: "field" }, h("label", { for: "move-note" }, "Not"), note),
      notice,
      h("div", null, add));

    form.addEventListener("submit", async event => {
      event.preventDefault();
      notice.classList.add("hidden");
      if (!location.value.trim()) {
        notice.textContent = "Konum zorunlu.";
        notice.classList.remove("hidden");
        return location.focus();
      }

      add.disabled = true;
      try {
        shipment = await api(`/api/shipments/${shipment.trackingNumber}/events`, {
          method: "POST",
          body: { status: status.value, location: location.value.trim(), note: note.value.trim() || null }
        });
        await render();
        toast("Hareket eklendi.");
        onChange();
      } catch (error) {
        if (!(error instanceof ApiError)) throw error;
        notice.textContent = error.message;
        notice.classList.remove("hidden");
        add.disabled = false;
      }
    });

    return h("div", { class: "block" }, h("h3", null, "Hareket ekle"), form);
  }

  try {
    await reload();
  } catch (error) {
    host.replaceChildren(h("div", { class: "notice error", role: "alert" }, error.message));
  }
}

function courierLabel(c) {
  const load = c.capacityKg != null
    ? `${Math.round(c.activeWeightKg)} / ${Math.round(c.capacityKg)} kg`
    : "araç yok";
  return `${c.name} (${c.vehiclePlate ?? "araçsız"}, ${load})`;
}
