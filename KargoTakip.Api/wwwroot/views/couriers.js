// Couriers (admin only). Every courier has a login, created together with the courier,
// and can be given one vehicle.

import { h, icon, api, toast, emptyState, formDialog, confirmDialog, formatKg, VEHICLE_TYPES } from "../ui.js";

export async function mount(container) {
  const body = h("div");

  container.replaceChildren(
    h("div", { class: "page-head" },
      h("h1", null, "Kuryeler"),
      h("div", { class: "actions" },
        h("button", { type: "button", class: "btn btn-primary", onclick: add }, icon("plus", 18), "Kurye ekle"))),
    h("div", { class: "panel" }, body));

  async function load() {
    const [couriers, vehicles] = await Promise.all([api("/api/couriers"), api("/api/vehicles")]);
    render(couriers, vehicles);
  }

  function reload() {
    load().catch(error => toast(error.message, "error"));
  }

  function render(couriers, vehicles) {
    body.replaceChildren(couriers.length === 0
      ? emptyState("Henüz kurye yok",
          "Kurye eklediğinde ona bir giriş hesabı da oluşturulur. Kurye kendi panelinde yalnızca kendisine atanan kargoları görür.",
          h("button", { type: "button", class: "btn btn-primary", onclick: add }, icon("plus", 18), "Kurye ekle"))
      : h("div", { class: "table-wrap" },
          h("table", { class: "data" },
            h("thead", null, h("tr", null,
              h("th", null, "Kurye"), h("th", null, "Araç"), h("th", null, "Taşıdığı yük"), h("th", null, ""))),
            h("tbody", null, couriers.map(c => row(c, vehicles))))));
  }

  function row(c, vehicles) {
    return h("tr", null,
      h("td", null, h("strong", null, c.name), h("span", { class: "cell-sub" }, [c.username ? `@${c.username}` : null, c.phone].filter(Boolean).join("  "))),
      h("td", null, c.vehiclePlate ?? h("span", { class: "muted" }, "Araç yok"),
        c.vehicleId ? h("span", { class: "cell-sub" }, vehicleSub(vehicles.find(v => v.id === c.vehicleId))) : null),
      h("td", { style: "min-width: 180px" }, loadBar(c)),
      h("td", { class: "actions" },
        h("button", { type: "button", class: "btn btn-sm btn-icon", "aria-label": `Düzenle: ${c.name}`, title: "Düzenle", onclick: () => edit(c, vehicles) }, icon("pencil", 16)),
        h("button", { type: "button", class: "btn btn-sm btn-icon btn-danger", "aria-label": `Sil: ${c.name}`, title: "Sil", onclick: () => remove(c) }, icon("trash", 16))));
  }

  const vehicleSub = v => v ? `${VEHICLE_TYPES[v.type] ?? v.type}, ${formatKg(v.capacityKg)} kapasite` : "";

  function loadBar(c) {
    if (c.capacityKg == null) return h("span", { class: "muted" }, `${c.activeShipments} açık kargo`);
    const pct = Math.min(100, Math.round((c.activeWeightKg / c.capacityKg) * 100));
    return h("div", null,
      h("div", { class: `load-bar ${pct >= 100 ? "full" : ""}`, role: "img", "aria-label": `Doluluk yüzde ${pct}` }, h("i", { style: `width: ${pct}%` })),
      h("span", { class: "cell-sub" }, `${c.activeShipments} açık kargo, ${formatKg(c.activeWeightKg)} / ${formatKg(c.capacityKg)}`));
  }

  /** Vehicles nobody drives yet, plus the one this courier already has. */
  const vehicleOptions = (vehicles, currentId) =>
    vehicles.filter(v => !v.courierId || v.id === currentId)
      .map(v => ({ value: v.id, label: `${v.plate} (${VEHICLE_TYPES[v.type] ?? v.type}, ${formatKg(v.capacityKg)})` }));

  async function add() {
    const vehicles = await api("/api/vehicles");
    const created = await formDialog({
      title: "Kurye ekle",
      intro: "Kurye bu kullanıcı adı ve şifreyle giriş yapıp kendisine atanan kargoların durumunu güncelleyebilir.",
      fields: [
        { name: "name", label: "Ad soyad", required: true, maxlength: 60, autocomplete: "off" },
        { name: "phone", label: "Telefon", type: "tel", maxlength: 20, inputmode: "tel" },
        { name: "vehicleId", label: "Araç", options: vehicleOptions(vehicles), placeholder: "Araç yok", span2: true },
        { name: "username", label: "Kullanıcı adı", required: true, maxlength: 30, autocapitalize: "none" },
        { name: "password", label: "Şifre", required: true, type: "password", autocomplete: "new-password", hint: "En az 8 karakter; harf ve rakam içermeli." }
      ],
      submitLabel: "Kurye ekle",
      onSubmit: values => api("/api/couriers", { method: "POST", body: values })
    });
    if (created && created !== true) {
      toast(`${created.name} eklendi.`);
      reload();
    }
  }

  async function edit(c, vehicles) {
    const updated = await formDialog({
      title: "Kuryeyi düzenle",
      fields: [
        { name: "name", label: "Ad soyad", required: true, maxlength: 60 },
        { name: "phone", label: "Telefon", type: "tel", maxlength: 20, inputmode: "tel" },
        { name: "vehicleId", label: "Araç", options: vehicleOptions(vehicles, c.vehicleId), placeholder: "Araç yok", span2: true }
      ],
      initial: c,
      onSubmit: values => api(`/api/couriers/${c.id}`, { method: "PUT", body: values })
    });
    if (updated && updated !== true) {
      toast("Kurye güncellendi.");
      reload();
    }
  }

  async function remove(c) {
    const ok = await confirmDialog({
      title: "Kuryeyi sil",
      message: `${c.name} ve giriş hesabı silinecek. Kuryenin açık kargosu varsa önce onları başka bir kuryeye atamalısın.`,
      confirmLabel: "Kuryeyi sil",
      danger: true
    });
    if (!ok) return;

    try {
      await api(`/api/couriers/${c.id}`, { method: "DELETE" });
      toast("Kurye silindi.");
      reload();
    } catch (error) {
      toast(error.message, "error");
    }
  }

  await load();
}
