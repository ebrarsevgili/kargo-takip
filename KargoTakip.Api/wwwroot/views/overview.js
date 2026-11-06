// Overview: how many shipments are at each stage of the flow, for the shipments this user may see.

import { h, icon, api, STATUS_FLOW, STATUS_LABELS, emptyState } from "../ui.js";

const WELCOME = {
  Admin: "Tüm kargoların aşama aşama dağılımı. Bir aşamaya tıklayınca o kargolar listelenir.",
  Courier: "Sana atanan kargoların durumu.",
  Customer: "Oluşturduğun kargoların durumu."
};

export async function mount(container, { me }) {
  const stats = await api("/api/shipments/stats");

  const cells = [...STATUS_FLOW, "Cancelled"].map(status =>
    h("a", { class: "stat", href: `#/kargolar?status=${status}` },
      h("span", { class: "figure" }, stats.byStatus[status] ?? 0),
      h("span", { class: "name" }, STATUS_LABELS[status])));

  if (stats.unassigned != null) {
    cells.push(h("a", { class: `stat ${stats.unassigned > 0 ? "attention" : ""}`, href: "#/kargolar?unassigned=1" },
      h("span", { class: "figure" }, stats.unassigned),
      h("span", { class: "name" }, "Kurye bekliyor")));
  }

  container.replaceChildren(
    h("div", { class: "page-head" },
      h("div", null, h("h1", null, `Merhaba, ${me.displayName.split(" ")[0]}`), h("p", { class: "muted" }, WELCOME[me.role]))),
    stats.total === 0
      ? h("div", { class: "panel" }, emptyState(
          "Henüz kargo yok",
          me.role === "Courier" ? "Yönetici sana kargo atadığında burada görünür." : "İlk kargoyu oluşturarak başla.",
          me.role === "Courier" ? null : h("a", { class: "btn btn-primary", href: "#/kargolar?yeni=1" }, icon("plus", 18), "Yeni kargo")))
      : h("div", { class: "stats" }, cells));
}
