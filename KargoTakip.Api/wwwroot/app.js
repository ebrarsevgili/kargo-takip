// Dashboard shell: sign-in check, role-based menu and a tiny hash router (#/ozet, #/kargolar?status=InTransit ...).
// Every page lives in views/*.js and exports mount(container, ctx), optionally returning a cleanup function.

import { h, icon, api, ApiError, ROLE_LABELS, setUnauthorizedHandler, toast } from "./ui.js";

// A menu entry appears only for the roles listed; the server enforces the same rules on every request.
const ROUTES = [
  { path: "ozet", label: () => "Özet", icon: "grid", roles: ["Admin", "Courier", "Customer"], load: () => import("./views/overview.js") },
  { path: "kargolar", label: user => ({ Admin: "Kargolar", Courier: "Teslimatlarım", Customer: "Kargolarım" })[user.role], icon: "box",
    roles: ["Admin", "Courier", "Customer"], load: () => import("./views/shipments.js") }
];

const rail = document.getElementById("rail");
const main = document.getElementById("main");

let me = null;
let cleanup = null;

setUnauthorizedHandler(() => location.replace("login.html"));

function parseHash() {
  const raw = location.hash.replace(/^#\/?/, "");
  const [path, query = ""] = raw.split("?");
  return { path: path || "ozet", params: new URLSearchParams(query) };
}

function navigate(hash) {
  if (location.hash === hash) window.dispatchEvent(new HashChangeEvent("hashchange"));
  else location.hash = hash;
}

function renderRail(current) {
  const items = ROUTES.filter(r => r.roles.includes(me.role));

  rail.replaceChildren(
    h("a", { class: "brand", href: "#/ozet" }, h("span", { class: "brand-mark" }), "Kargo Takip"),
    h("nav", { class: "nav" },
      items.map(r => h("a", { href: `#/${r.path}`, "aria-current": r.path === current ? "page" : null }, icon(r.icon), r.label(me)))),
    h("div", { class: "rail-foot" },
      h("div", { class: "who" }, h("strong", null, me.displayName), h("span", null, ROLE_LABELS[me.role] ?? me.role)),
      h("button", { type: "button", class: "btn btn-sm", onclick: logout }, icon("logout", 16), "Çıkış yap")));
}

async function logout() {
  try { await api("/api/auth/logout", { method: "POST" }); } catch { /* leave anyway */ }
  location.replace("login.html");
}

async function render() {
  const { path, params } = parseHash();
  const route = ROUTES.find(r => r.path === path && r.roles.includes(me.role)) ?? ROUTES[0];

  cleanup?.();
  cleanup = null;
  renderRail(route.path);
  document.title = `${route.label(me)} - Kargo Takip`;
  main.replaceChildren();

  try {
    const view = await route.load();
    cleanup = (await view.mount(main, { me, params, navigate })) ?? null;
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return;   // the unauthorized handler is redirecting
    main.replaceChildren(h("div", { class: "notice error", role: "alert" }, error.message ?? "Sayfa yüklenemedi."));
    toast("Sayfa yüklenemedi.", "error");
  }
  main.focus({ preventScroll: true });
}

window.addEventListener("hashchange", render);

try {
  me = await api("/api/auth/me");
  await render();
} catch (error) {
  if (!(error instanceof ApiError && error.status === 401)) {
    main.replaceChildren(h("div", { class: "notice error", role: "alert" }, error.message));
  }
}
