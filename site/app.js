// auros.ram — landing.
// COMPLETAR antes de publicar: precio, link de pago y email de ventas.
const CONFIG = {
  version: "2.3.0",
  repo: "jpfinocchiaro/auros.ram.setup",
  precio: "",       // por ejemplo: "USD 120" (se muestra igual en todos los idiomas)
  pagoUrl: "",      // link de pago generado en Mercado Pago o Stripe
  email: "",        // email donde se reciben los códigos de equipo
  mac: false,       // true cuando los .dmg de Mac estén subidos a la release de GitHub
};

const LANGS = { es: "es", en: "us", pt: "br", fr: "fr" }; // idioma → bandera (como en la aplicación)
const STORE_LANG = "auros.lang";

// ---- Almacenamiento local (puede no estar disponible) --------------------------------
function load(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}
function save(key, value) {
  try { localStorage.setItem(key, value); } catch { /* sin almacenamiento: no pasa nada */ }
}

// ---- Idioma ---------------------------------------------------------------------------
function pickLanguage() {
  const fromUrl = new URLSearchParams(location.search).get("lang");
  if (fromUrl && LANGS[fromUrl]) return fromUrl;
  const saved = load(STORE_LANG);
  if (saved && LANGS[saved]) return saved;
  for (const tag of navigator.languages || [navigator.language || ""]) {
    const code = tag.slice(0, 2).toLowerCase();
    if (LANGS[code]) return code;
  }
  return "es";
}

let lang = pickLanguage();
const t = (key) => I18N[lang][key] ?? I18N.es[key] ?? key;
const fmt = (text, ...args) => text.replace(/\{(\d)\}/g, (_, i) => args[Number(i)] ?? "");

function setLanguage(code) {
  lang = code;
  save(STORE_LANG, code);
  render();
}

// ---- Sistema de quien visita ------------------------------------------------------------
const ua = navigator.userAgent;
const isMac = /Mac/i.test(navigator.platform || ua) && !/iPhone|iPad/i.test(ua);
const isWindows = /Win/i.test(navigator.platform || ua);
const fileUrl = (file) => `https://github.com/${CONFIG.repo}/releases/latest/download/auros.ram-${CONFIG.version}-${file}`;

// ---- Mapa de calor de la portada ----------------------------------------------------------
// Filas: impacto de Catastrófico (0) a Insignificante (4). Columnas: probabilidad de Rara (0) a Casi segura (4).
const LEVELS = [
  ["low", "mid", "high", "high", "high"],
  ["low", "mid", "mid", "high", "high"],
  ["low", "low", "mid", "mid", "high"],
  ["low", "low", "low", "mid", "mid"],
  ["low", "low", "low", "low", "low"],
];
// Posición [impacto, probabilidad] de cada riesgo en cada fecha (null = todavía no evaluado).
const POSITIONS = [
  { jun: [2, 2], oct: [1, 3] },
  { jun: [1, 3], oct: [1, 1] },
  { jun: [0, 3], oct: [2, 1] },
  { jun: null, oct: [1, 4] },
  { jun: [0, 1], oct: [0, 1] },
  { jun: [2, 3], oct: [2, 3] },
];

const grid = document.getElementById("grid");
const cells = [];
for (let r = 0; r < 5; r++) {
  for (let c = 0; c < 5; c++) {
    const cell = document.createElement("div");
    cell.className = `cell ${LEVELS[r][c]}`;
    grid.appendChild(cell);
    cells.push(cell);
  }
}
const dots = POSITIONS.map((_, i) => {
  const dot = document.createElement("span");
  dot.className = "dot";
  dot.textContent = String(i + 1);
  grid.appendChild(dot);
  return dot;
});
let date = "jun";

function place() {
  const slots = new Map();
  POSITIONS.forEach((p, i) => {
    const pos = p[date];
    const dot = dots[i];
    dot.title = t("risks")[i];
    if (!pos) {
      dot.classList.add("gone");
      return;
    }
    dot.classList.remove("gone");
    const key = pos.join(",");
    const slot = slots.get(key) ?? 0;
    slots.set(key, slot + 1);
    const cell = cells[pos[0] * 5 + pos[1]];
    const x = cell.offsetLeft + 6 + slot * 22;
    const y = cell.offsetTop + (cell.offsetHeight - 26) / 2;
    dot.style.transform = `translate(${x}px, ${y}px)`;
  });

  document.getElementById("legend").replaceChildren(...POSITIONS.map((p, i) => {
    const pos = p[date];
    const level = pos ? LEVELS[pos[0]][pos[1]] : null;
    const li = document.createElement("li");
    const n = Object.assign(document.createElement("span"), { className: "n", textContent: String(i + 1) });
    const name = Object.assign(document.createElement("span"), { className: "name", textContent: t("risks")[i] });
    const pill = Object.assign(document.createElement("span"), {
      className: `pill ${level ?? "none"}`,
      textContent: t(level ? `level.${level}` : "level.none"),
    });
    li.append(n, name, pill);
    return li;
  }));
}

const seg = document.querySelectorAll(".seg [role=radio]");
for (const button of seg) {
  button.addEventListener("click", () => {
    for (const b of seg) b.setAttribute("aria-checked", String(b === button));
    date = button.dataset.date;
    place();
  });
}

// ---- Capturas -----------------------------------------------------------------------------
let shot = "matriz";
const tabs = document.querySelectorAll(".tabs [role=tab]");
for (const tab of tabs) {
  tab.addEventListener("click", () => {
    for (const x of tabs) x.setAttribute("aria-selected", String(x === tab));
    shot = tab.dataset.shot;
    renderShot();
  });
}
function renderShot() {
  const img = document.getElementById("shot-img");
  img.src = `img/${shot}.webp`;
  img.alt = t(`shots.alt.${shot}`);
  document.getElementById("shot-caption").textContent = t(`shots.cap.${shot}`);
  document.getElementById("shot-lang").textContent = t("shots.lang");
}

// ---- Textos de toda la página ---------------------------------------------------------------
function el(tag, props = {}, ...children) {
  const node = Object.assign(document.createElement(tag), props);
  node.append(...children);
  return node;
}

function render() {
  document.documentElement.lang = lang;
  document.title = t("meta.title");
  document.querySelector('meta[name="description"]').content = t("meta.description");

  for (const node of document.querySelectorAll("[data-i18n]")) node.textContent = t(node.dataset.i18n);
  for (const node of document.querySelectorAll("[data-i18n-html]")) node.innerHTML = t(node.dataset.i18nHtml);
  for (const node of document.querySelectorAll("[data-i18n-aria]")) node.setAttribute("aria-label", t(node.dataset.i18nAria));

  // Selector de idioma
  document.getElementById("lang-flag").src = `img/flags/${LANGS[lang]}.svg`;
  document.getElementById("lang-code").textContent = lang.toUpperCase();
  for (const item of document.querySelectorAll("[data-lang]")) {
    item.setAttribute("aria-checked", String(item.dataset.lang === lang));
  }

  // Ejes, funciones, precios y preguntas
  document.getElementById("levels-y").replaceChildren(...t("imp").map((s) => el("li", { textContent: s })));
  document.getElementById("levels-x").replaceChildren(...t("prob").map((s) => el("li", { textContent: s })));
  document.getElementById("feature-list").replaceChildren(...t("feat").map(([title, text]) =>
    el("div", {}, el("dt", { textContent: title }), el("dd", { textContent: text }))));
  document.getElementById("price-rows").replaceChildren(...t("price.rows").map(([name, free, full]) =>
    el("tr", {}, el("th", { scope: "row", textContent: name }), el("td", { textContent: free }),
      el("td", { className: "full", textContent: full }))));
  document.getElementById("faq-list").replaceChildren(...t("faq").map(([q, a]) =>
    el("details", {}, el("summary", { textContent: q }), el("p", { innerHTML: a }))));
  document.getElementById("price").textContent = CONFIG.precio || t("price.tbd");

  // Descargas
  document.getElementById("version").textContent = CONFIG.version;
  for (const li of document.querySelectorAll(".files li")) {
    li.querySelector(".yours")?.remove();
    const mine = (isWindows && li.dataset.os === "windows") || (isMac && li.dataset.os === "mac");
    if (mine) li.querySelector("h3").append(el("span", { className: "yours", textContent: t("dl.yours") }));
    const link = li.querySelector("a");
    if (li.dataset.os === "mac" && !CONFIG.mac) {
      link.textContent = t("dl.soon");
      link.removeAttribute("href");
      link.setAttribute("aria-disabled", "true");
      link.classList.add("btn-ghost");
    } else {
      link.href = fileUrl(link.dataset.file);
    }
  }
  const primary = document.getElementById("cta-primary");
  if (isWindows) {
    primary.textContent = t("hero.cta.win");
    primary.href = fileUrl("win-x64.msi");
  } else if (isMac && CONFIG.mac) {
    primary.textContent = t("hero.cta.mac");
  }
  document.getElementById("cta-note").textContent = t(CONFIG.mac ? "hero.note" : "hero.note.nomac");

  // Pago y contacto
  const pay = document.getElementById("pay-link");
  if (CONFIG.pagoUrl) {
    Object.assign(pay, { href: CONFIG.pagoUrl, target: "_blank", rel: "noopener" });
  } else {
    pay.setAttribute("aria-disabled", "true");
  }
  const mailto = CONFIG.email
    ? `mailto:${CONFIG.email}?subject=${encodeURIComponent(t("mail.subject"))}&body=${encodeURIComponent(t("mail.body"))}`
    : null;
  const mail = document.getElementById("mail-link");
  if (mailto) mail.href = mailto; else mail.setAttribute("aria-disabled", "true");
  if (CONFIG.email) document.getElementById("foot-mail").href = `mailto:${CONFIG.email}`;

  renderShot();
  place();
}

// ---- Menú de idiomas --------------------------------------------------------------------
const langBtn = document.getElementById("lang-btn");
const langMenu = document.getElementById("lang-menu");
function toggleMenu(open) {
  langMenu.hidden = !open;
  langBtn.setAttribute("aria-expanded", String(open));
  if (open) langMenu.querySelector(`[data-lang="${lang}"]`).focus();
}
langBtn.addEventListener("click", () => toggleMenu(langMenu.hidden));
for (const item of langMenu.querySelectorAll("[data-lang]")) {
  item.addEventListener("click", () => {
    setLanguage(item.dataset.lang);
    toggleMenu(false);
    langBtn.focus();
  });
}
document.addEventListener("click", (e) => {
  if (!langMenu.hidden && !e.target.closest(".lang")) toggleMenu(false);
});
langMenu.addEventListener("keydown", (e) => {
  const items = [...langMenu.querySelectorAll("[data-lang]")];
  const i = items.indexOf(document.activeElement);
  if (e.key === "Escape") { toggleMenu(false); langBtn.focus(); }
  if (e.key === "ArrowDown") { e.preventDefault(); items[(i + 1) % items.length].focus(); }
  if (e.key === "ArrowUp") { e.preventDefault(); items[(i - 1 + items.length) % items.length].focus(); }
});

// ---- Inicio ----------------------------------------------------------------------------
for (const dot of dots) dot.style.transition = "none";
render();
requestAnimationFrame(() => requestAnimationFrame(() => {
  for (const dot of dots) dot.style.transition = "";
}));
window.addEventListener("resize", place);
