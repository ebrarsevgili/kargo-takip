// Vehicles (admin only): plate, type and the weight the vehicle can carry.
// The capacity is what the courier assignment checks against.

import { h, icon, api, toast, emptyState, formDialog, confirmDialog, formatKg, VEHICLE_TYPES } from "../ui.js";

const TYPE_OPTIONS = Object.entries(VEHICLE_TYPES).map(([value, label]) => ({ value, label }));

export async function mount(container) {
  const body = h("div");

  container.replaceChildren(
    h("div", { class: "page-head" },
      h("h1", null, "Araçlar"),
      h("div", { class: "actions" },
        h("button", { type: "button", class: "btn btn-primary", onclick: add }, icon("plus", 18), "Araç ekle"))),
    h("div", { class: "panel" }, body));

  async function load() {
    render(await api("/api/vehicles"));
  }

  function reload() {
    load().catch(error => toast(error.message, "error"));
  }

  function render(vehicles) {
    body.replaceChildren(vehicles.length === 0
      ? emptyState("Henüz araç yok", "Araç eklediğinde bir kuryeye verebilir ve kargo atarken kapasitesinin aşılmadığından emin olabilirsin.",
          h("button", { type: "button", class: "btn btn-primary", onclick: add }, icon("plus", 18), "Araç ekle"))
      : h("div", { class: "table-wrap" },
          h("table", { class: "data" },
            h("thead", null, h("tr", null,
              h("th", null, "Plaka"), h("th", null, "Tip"), h("th", { class: "num" }, "Kapasite"), h("th", null, "Kurye"), h("th", null, ""))),
            h("tbody", null, vehicles.map(row)))));
  }

  function row(v) {
    return h("tr", null,
      h("td", null, h("strong", { class: "display", style: "font-size: 1.125rem; letter-spacing: 0.04em" }, v.plate)),
      h("td", null, VEHICLE_TYPES[v.type] ?? v.type),
      h("td", { class: "num" }, formatKg(v.capacityKg)),
      h("td", null, v.courierName ?? h("span", { class: "muted" }, "Boşta")),
      h("td", { class: "actions" },
        h("button", { type: "button", class: "btn btn-sm btn-icon", "aria-label": `Düzenle: ${v.plate}`, title: "Düzenle", onclick: () => edit(v) }, icon("pencil", 16)),
        h("button", { type: "button", class: "btn btn-sm btn-icon btn-danger", "aria-label": `Sil: ${v.plate}`, title: "Sil", onclick: () => remove(v) }, icon("trash", 16))));
  }

  const fields = () => [
    { name: "plate", label: "Plaka", required: true, maxlength: 12, hint: "Örnek: 34 ABC 123", autocapitalize: "characters" },
    { name: "type", label: "Araç tipi", required: true, options: TYPE_OPTIONS },
    { name: "capacityKg", label: "Taşıma kapasitesi (kg)", required: true, number: true, inputmode: "decimal", span2: true }
  ];

  async function add() {
    const created = await formDialog({
      title: "Araç ekle",
      fields: fields(),
      initial: { type: "van" },
      submitLabel: "Araç ekle",
      onSubmit: values => api("/api/vehicles", { method: "POST", body: values })
    });
    if (created && created !== true) {
      toast(`${created.plate} eklendi.`);
      reload();
    }
  }

  async function edit(v) {
    const updated = await formDialog({
      title: "Aracı düzenle",
      fields: fields(),
      initial: { ...v, capacityKg: String(v.capacityKg).replace(".", ",") },
      onSubmit: values => api(`/api/vehicles/${v.id}`, { method: "PUT", body: values })
    });
    if (updated && updated !== true) {
      toast("Araç güncellendi.");
      reload();
    }
  }

  async function remove(v) {
    const ok = await confirmDialog({
      title: "Aracı sil",
      message: `${v.plate} plakalı araç silinecek. Bir kuryeye verilmiş araç silinemez.`,
      confirmLabel: "Aracı sil",
      danger: true
    });
    if (!ok) return;

    try {
      await api(`/api/vehicles/${v.id}`, { method: "DELETE" });
      toast("Araç silindi.");
      reload();
    } catch (error) {
      toast(error.message, "error");
    }
  }

  await load();
}
