// Guia do REA Advanced Suite — navegação, busca, tema, cópia de código e ampliação de imagens.
(function () {
  "use strict";

  const storage = {
    get(key) {
      try {
        return window.localStorage.getItem(key);
      } catch {
        return null;
      }
    },
    set(key, value) {
      try {
        window.localStorage.setItem(key, value);
      } catch {
        /* sem armazenamento: ignora */
      }
    },
  };

  // ---------- Tema ----------
  const root = document.documentElement;
  const themeBtn = document.getElementById("theme");
  const savedTheme = storage.get("rea-guide-theme");
  if (savedTheme === "light" || savedTheme === "dark")
    root.dataset.theme = savedTheme;
  function currentTheme() {
    if (root.dataset.theme) return root.dataset.theme;
    return window.matchMedia("(prefers-color-scheme: dark)").matches
      ? "dark"
      : "light";
  }
  function paintThemeButton() {
    themeBtn.textContent =
      currentTheme() === "dark" ? "Tema claro" : "Tema escuro";
  }
  themeBtn.addEventListener("click", () => {
    root.dataset.theme = currentTheme() === "dark" ? "light" : "dark";
    storage.set("rea-guide-theme", root.dataset.theme);
    paintThemeButton();
  });
  paintThemeButton();

  // ---------- Menu lateral gerado a partir das seções ----------
  const nav = document.getElementById("toc");
  const sections = Array.from(
    document.querySelectorAll("main section[data-group]"),
  );
  let lastGroup = null;
  const links = [];
  for (const section of sections) {
    if (section.dataset.group !== lastGroup) {
      lastGroup = section.dataset.group;
      const g = document.createElement("div");
      g.className = "group";
      g.textContent = lastGroup;
      nav.appendChild(g);
    }
    const h2 = section.querySelector("h2");
    const a = document.createElement("a");
    a.href = "#" + section.id;
    a.textContent =
      section.dataset.title ||
      (h2 ? h2.firstChild.textContent.trim() : section.id);
    const status = section.dataset.status;
    if (status) {
      const b = document.createElement("span");
      b.className = "badge " + (status === "pronto" ? "ok" : "planned");
      b.textContent = status === "pronto" ? "pronto" : "planejado";
      a.appendChild(b);
    }
    a.dataset.target = section.id;
    nav.appendChild(a);
    links.push(a);
    for (const h3 of section.querySelectorAll("h3[id]")) {
      const sub = document.createElement("a");
      sub.href = "#" + h3.id;
      sub.className = "sub";
      sub.textContent = h3.textContent;
      sub.dataset.parent = section.id;
      nav.appendChild(sub);
    }
  }

  // Seção ativa conforme a rolagem
  const observer = new IntersectionObserver(
    (entries) => {
      for (const e of entries) {
        if (!e.isIntersecting) continue;
        for (const l of links)
          l.classList.toggle("active", l.dataset.target === e.target.id);
      }
    },
    { rootMargin: "-70px 0px -70% 0px" },
  );
  sections.forEach((s) => observer.observe(s));

  // Menu no celular
  const sidebar = document.querySelector("nav.sidebar");
  document
    .getElementById("menu")
    .addEventListener("click", () => sidebar.classList.toggle("open"));
  nav.addEventListener("click", (e) => {
    if (e.target.closest("a")) sidebar.classList.remove("open");
  });

  // ---------- Busca ----------
  const search = document.getElementById("q");
  const noResults = document.getElementById("no-results");
  function normalize(s) {
    return s
      .normalize("NFD")
      .replace(/[̀-ͯ]/g, "")
      .toLowerCase();
  }
  search.addEventListener("input", () => {
    const q = normalize(search.value.trim());
    let visible = 0;
    for (const section of sections) {
      const match = !q || normalize(section.textContent).includes(q);
      section.classList.toggle("hidden", !match);
      if (match) visible++;
      for (const a of nav.querySelectorAll(
        `a[data-target="${section.id}"], a[data-parent="${section.id}"]`,
      )) {
        a.classList.toggle("hidden", !match);
      }
    }
    noResults.classList.toggle("show", visible === 0);
  });
  document.addEventListener("keydown", (e) => {
    if (e.key === "/" && document.activeElement !== search) {
      e.preventDefault();
      search.focus();
    }
    if (e.key === "Escape") {
      search.value = "";
      search.dispatchEvent(new Event("input"));
      lightbox.classList.remove("open");
    }
  });

  // ---------- Copiar código ----------
  for (const pre of document.querySelectorAll("pre")) {
    const btn = document.createElement("button");
    btn.className = "copy";
    btn.type = "button";
    btn.textContent = "Copiar";
    btn.addEventListener("click", async () => {
      const text = pre.querySelector("code")?.innerText ?? pre.innerText;
      try {
        await navigator.clipboard.writeText(text);
        btn.textContent = "Copiado";
      } catch {
        btn.textContent = "Selecione e copie";
      }
      setTimeout(() => (btn.textContent = "Copiar"), 1600);
    });
    pre.appendChild(btn);
  }

  // ---------- Ampliar imagens ----------
  const lightbox = document.getElementById("lightbox");
  const lightImg = lightbox.querySelector("img");
  for (const img of document.querySelectorAll("figure img")) {
    img.addEventListener("click", () => {
      lightImg.src = img.src;
      lightImg.alt = img.alt;
      lightbox.classList.add("open");
    });
  }
  lightbox.addEventListener("click", () => lightbox.classList.remove("open"));
})();
