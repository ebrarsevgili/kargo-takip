// Sign in and sign up. Registering always creates a customer account; couriers and admins are created by an admin.

import { h, api, ApiError } from "./ui.js";

const notice = document.getElementById("notice");
const title = document.getElementById("title");
const subtitle = document.getElementById("subtitle");
const loginForm = document.getElementById("login-form");
const registerForm = document.getElementById("register-form");
const tabs = { login: document.getElementById("tab-login"), register: document.getElementById("tab-register") };

function field(label, name, { type = "text", autocomplete, hint } = {}) {
  const id = `${name}-${Math.random().toString(36).slice(2, 7)}`;
  const message = h("div", { class: "field-error hidden", id: `${id}-msg` });
  const input = h("input", { id, name, type, autocomplete, autocapitalize: "none", spellcheck: "false", "aria-describedby": `${id}-msg` });
  return { name, input, message, node: h("div", { class: "field" }, h("label", { for: id }, label), input, hint ? h("div", { class: "hint" }, hint) : null, message) };
}

function setup(form, fields, submitLabel, request) {
  const submit = h("button", { type: "submit", class: "btn btn-primary" }, submitLabel);
  form.append(...fields.map(f => f.node), submit);

  form.addEventListener("submit", async event => {
    event.preventDefault();
    notice.classList.add("hidden");
    for (const f of fields) { f.message.classList.add("hidden"); f.input.setAttribute("aria-invalid", "false"); }

    const values = Object.fromEntries(fields.map(f => [f.name, f.input.value]));
    const empty = fields.find(f => !values[f.name].trim());
    if (empty) {
      empty.message.textContent = "Bu alan zorunlu.";
      empty.message.classList.remove("hidden");
      empty.input.setAttribute("aria-invalid", "true");
      return empty.input.focus();
    }

    submit.disabled = true;
    try {
      await api(request, { method: "POST", body: values });
      location.replace("app.html");
    } catch (error) {
      if (!(error instanceof ApiError)) throw error;
      let shown = false;
      for (const [key, messages] of Object.entries(error.fields ?? {})) {
        const f = fields.find(x => x.name.toLowerCase() === key.toLowerCase());
        if (f) {
          f.message.textContent = [messages].flat().join(" ");
          f.message.classList.remove("hidden");
          f.input.setAttribute("aria-invalid", "true");
          shown = true;
        }
      }
      if (!shown) {
        notice.textContent = error.message;
        notice.classList.remove("hidden");
      }
      submit.disabled = false;
    }
  });
}

setup(loginForm, [
  field("Kullanıcı adı", "username", { autocomplete: "username" }),
  field("Şifre", "password", { type: "password", autocomplete: "current-password" })
], "Giriş yap", "/api/auth/login");

setup(registerForm, [
  field("Ad soyad", "displayName", { autocomplete: "name" }),
  field("Kullanıcı adı", "username", { autocomplete: "username", hint: "Küçük harf, rakam, nokta, tire veya alt çizgi. En az 3 karakter." }),
  field("Şifre", "password", { type: "password", autocomplete: "new-password", hint: "En az 8 karakter; en az bir harf ve bir rakam içermeli." })
], "Hesap oluştur", "/api/auth/register");

function select(which) {
  const isLogin = which === "login";
  tabs.login.setAttribute("aria-selected", String(isLogin));
  tabs.register.setAttribute("aria-selected", String(!isLogin));
  loginForm.classList.toggle("hidden", !isLogin);
  registerForm.classList.toggle("hidden", isLogin);
  notice.classList.add("hidden");
  title.textContent = isLogin ? "Hesabına giriş yap" : "Müşteri hesabı oluştur";
  subtitle.textContent = isLogin
    ? "Kargolarını yönetmek ve yeni gönderi oluşturmak için giriş yap."
    : "Hesapla gönderi oluşturabilir, kargolarını tek yerden izleyebilirsin.";
  document.title = `${isLogin ? "Giriş yap" : "Kayıt ol"} - Kargo Takip`;
}

tabs.login.addEventListener("click", () => select("login"));
tabs.register.addEventListener("click", () => select("register"));
if (new URLSearchParams(location.search).get("tab") === "register") select("register");

// Already signed in? Go straight to the dashboard.
api("/api/auth/me").then(() => location.replace("app.html")).catch(() => { /* not signed in */ });
