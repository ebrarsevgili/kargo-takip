// Shipment list for every role. The server already limits what each role gets back
// (admin: all, courier: assigned to them, customer: their own), so this page only decides what to show.

import {
  h, icon, api, debounce, toast, statusChip, routeStrip, emptyState, formatDay, formatKg,
  formDialog, STATUS_LABELS
} from "../ui.js";
import { CITY_NAMES } from "../cities.js";

const TITLES = { Admin: "Kargolar", Courier: "Teslimatlarım", Customer: "Kargolarım" };
const PAGE_SIZE = 10;

export async function mount(container, ctx) {
  const { me } = ctx;
  const isAdmin = me.role === "Admin";
  const canCreate = me.role !== "Courier";

  const state = {
    q: ctx.params.get("q") ?? "",
    status: ctx.params.get("status") ?? "",
    unassigned: ctx.params.get("unassigned") === "1",
    page: Number(ctx.params.get("page")) || 1
  };

  // One shared list of city names for every city input on this page.
  const cityList = h("datalist", { id: "city-list" }, CITY_NAMES.map(name => h("option", { value: name })));

  const searchInput = h("input", { type: "search", placeholder: "Takip no, alıcı, şehir veya telefon ara", "aria-label": "Kargo ara", value: state.q, autocomplete: "off" });
  const statusSelect = h("select", { "aria-label": "Duruma göre filtrele" },
    h("option", { value: "" }, "Tüm durumlar"),
    Object.entries(STATUS_LABELS).map(([value, label]) => h("option", { value }, label)));
  statusSelect.value = state.status;
  const unassignedToggle = isAdmin
    ? h("button", { type: "button", class: "btn", "aria-pressed": String(state.unassigned) }, "Kurye bekleyenler")
    : null;

  const body = h("div", { "aria-live": "polite" });
  const pager = h("div", { class: "pager hidden" });

  const toolbar = h("div", { class: "toolbar" },
    h("div", { class: "search" }, icon("search", 18), searchInput),
    statusSelect,
    unassignedToggle);

  container.replaceChildren(
    cityList,
    h("div", { class: "page-head" },
      h("h1", null, TITLES[me.role]),
      h("div", { class: "actions" },
        canCreate ? h("button", { type: "button", class: "btn btn-primary", onclick: openCreate }, icon("plus", 18), "Yeni kargo") : null)),
    h("div", { class: "panel" }, toolbar, body, pager));

  let requestId = 0;

  function syncHash() {
    const query = new URLSearchParams();
    if (state.q) query.set("q", state.q);
    if (state.status) query.set("status", state.status);
    if (state.unassigned) query.set("unassigned", "1");
    if (state.page > 1) query.set("page", String(state.page));
    const text = query.toString();
    history.replaceState(null, "", `#/kargolar${text ? `?${text}` : ""}`);   // shareable, but no new history entry per keystroke
  }

  async function load() {
    const mine = ++requestId;
    const query = new URLSearchParams({ page: String(state.page), pageSize: String(PAGE_SIZE) });
    if (state.q) query.set("q", state.q);
    if (state.status) query.set("status", state.status);
    if (state.unassigned) query.set("unassigned", "true");

    const result = await api(`/api/shipments?${query}`);
    if (mine !== requestId) return;   // a newer request is already on its way

    state.page = result.page;
    syncHash();
    renderTable(result);
  }

  function reload() {
    load().catch(error => toast(error.message, "error"));
  }

  function renderTable(result) {
    const filtering = state.q || state.status || state.unassigned;

    if (result.total === 0) {
      pager.classList.add("hidden");
      body.replaceChildren(filtering
        ? emptyState("Sonuç bulunamadı", "Arama veya filtreyi değiştirip tekrar dene.",
            h("button", { type: "button", class: "btn", onclick: clearFilters }, "Filtreleri temizle"))
        : emptyState(
            me.role === "Courier" ? "Sana atanmış kargo yok" : "Henüz kargo yok",
            me.role === "Courier" ? "Yönetici bir kargo atadığında burada görünür." : "İlk kargoyu oluşturarak başla.",
            canCreate ? h("button", { type: "button", class: "btn btn-primary", onclick: openCreate }, icon("plus", 18), "Yeni kargo") : null));
      return;
    }

    body.replaceChildren(h("div", { class: "table-wrap" },
      h("table", { class: "data" },
        h("thead", null, h("tr", null,
          h("th", null, "Takip no"),
          h("th", null, "Alıcı"),
          h("th", { class: "hide-sm" }, "Güzergâh"),
          h("th", null, "Durum"),
          isAdmin ? h("th", { class: "hide-sm" }, "Kurye") : null,
          h("th", { class: "hide-sm" }, "Kayıt"))),
        h("tbody", null, result.items.map(row)))));

    const from = (result.page - 1) * result.pageSize + 1;
    const to = from + result.items.length - 1;
    pager.classList.toggle("hidden", result.totalPages <= 1 && result.total <= PAGE_SIZE);
    pager.replaceChildren(
      h("span", { class: "muted" }, `${from}-${to} / ${result.total} kargo`),
      h("div", { class: "pager-buttons" },
        h("button", { type: "button", class: "btn btn-sm", disabled: result.page <= 1, "aria-label": "Önceki sayfa", onclick: () => goTo(result.page - 1) }, icon("chevronLeft", 16), "Önceki"),
        h("span", { class: "muted" }, `${result.page} / ${result.totalPages}`),
        h("button", { type: "button", class: "btn btn-sm", disabled: result.page >= result.totalPages, "aria-label": "Sonraki sayfa", onclick: () => goTo(result.page + 1) }, "Sonraki", icon("chevronRight", 16))));
  }

  function row(item) {
    return h("tr", null,
      h("td", null, h("a", { class: "link-btn", href: `index.html?no=${encodeURIComponent(item.trackingNumber)}`, target: "_blank", rel: "noopener" }, item.trackingNumber)),
      h("td", null, item.receiverName, item.receiverPhone ? h("span", { class: "cell-sub" }, item.receiverPhone) : null),
      h("td", { class: "hide-sm", style: "min-width: 220px" }, routeStrip(item.originCity, item.destinationCity, item.status),
        h("span", { class: "cell-sub" }, formatKg(item.weightKg))),
      h("td", null, statusChip(item.status)),
      isAdmin ? h("td", { class: "hide-sm" }, item.courierName ?? (isClosed(item) ? "-" : h("span", { class: "muted" }, "Atanmadı"))) : null,
      h("td", { class: "hide-sm" }, formatDay(item.createdAt)));
  }

  const isClosed = item => item.status === "Delivered" || item.status === "Cancelled";

  function goTo(page) {
    state.page = page;
    reload();
    container.scrollIntoView({ block: "start" });
  }

  function clearFilters() {
    Object.assign(state, { q: "", status: "", unassigned: false, page: 1 });
    searchInput.value = "";
    statusSelect.value = "";
    unassignedToggle?.setAttribute("aria-pressed", "false");
    reload();
  }

  searchInput.addEventListener("input", debounce(() => {
    state.q = searchInput.value.trim();
    state.page = 1;
    reload();
  }, 300));

  statusSelect.addEventListener("change", () => {
    state.status = statusSelect.value;
    state.page = 1;
    reload();
  });

  unassignedToggle?.addEventListener("click", () => {
    state.unassigned = !state.unassigned;
    unassignedToggle.setAttribute("aria-pressed", String(state.unassigned));
    state.page = 1;
    reload();
  });

  async function openCreate() {
    const fields = [
      { name: "senderName", label: "Gönderen", required: isAdmin, maxlength: 100, hint: isAdmin ? null : "Boş bırakırsan adın yazılır." },
      { name: "receiverName", label: "Alıcı", required: true, maxlength: 100 },
      { name: "receiverPhone", label: "Alıcı telefonu", type: "tel", maxlength: 20, inputmode: "tel" },
      { name: "weightKg", label: "Ağırlık (kg)", required: true, number: true, inputmode: "decimal", placeholder: "2,5" },
      { name: "originCity", label: "Çıkış şehri", required: true, maxlength: 80, list: "city-list" },
      { name: "destinationCity", label: "Varış şehri", required: true, maxlength: 80, list: "city-list" }
    ];
    if (isAdmin) {
      fields.push({ name: "ownerUsername", label: "Müşteri hesabı", span2: true, maxlength: 30, autocapitalize: "none",
        hint: "İstersen kargoyu bir müşterinin hesabına bağla; müşteri kendi panelinde görür." });
    }

    const created = await formDialog({
      title: "Yeni kargo",
      fields,
      initial: isAdmin ? {} : { senderName: me.displayName },
      submitLabel: "Kargo oluştur",
      onSubmit: values => api("/api/shipments", { method: "POST", body: values })
    });

    if (created && created !== true) {
      toast(`Kargo oluşturuldu: ${created.trackingNumber}`);
      state.page = 1;
      reload();
    }
  }

  await load();
  if (ctx.params.get("yeni") === "1" && canCreate) openCreate();
}
