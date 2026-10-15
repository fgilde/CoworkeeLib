// Starter specs and the mini wizard of getting-started/gallery. Plain JS on purpose: the page has to work from the static site alone.
(function () {
  "use strict";
  var assets = new URL(".", document.currentScript.src);
  var de = document.documentElement.lang === "de";
  var t = de ? {
    download: "Spec herunterladen", copy: "Kopieren", copied: "Kopiert", wizard: "Im Mini-Assistenten öffnen",
    none: "Keine Vorlage passt. Leere die Suche oder nimm den Tag weg.", failed: "Die Vorlagen ließen sich nicht laden.",
    valid: "Passt zum Schema der Spec.", blank: "Leer (nur die Optionen)", off: "aus (kein Assistent)", nameRule: "Ein Buchstabe, dann nur Buchstaben oder Ziffern.",
    noLanguage: "Mindestens eine Sprache wählen."
  } : {
    download: "Download spec", copy: "Copy", copied: "Copied", wizard: "Open in the mini wizard",
    none: "No template matches. Clear the search or the tag.", failed: "The templates could not be loaded.",
    valid: "Valid against the spec schema.", blank: "Blank (options only)", off: "off (no assistant)", nameRule: "A letter, then letters or digits only.",
    noLanguage: "Pick at least one language."
  };
  var specs = {};

  function load(path) {
    return fetch(new URL(path, assets)).then(function (r) {
      if (!r.ok) { throw new Error(path + ": " + r.status); }
      return r.json();
    });
  }

  function spec(id) {
    return specs[id] ? Promise.resolve(specs[id]) : load("specs/" + id + ".json").then(function (s) { return (specs[id] = s); });
  }

  function el(tag, attrs, children) {
    var node = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (k) {
      if (k === "text") { node.textContent = attrs[k]; } else { node.setAttribute(k, attrs[k]); }
    });
    (children || []).forEach(function (c) { if (c) { node.appendChild(c); } });
    return node;
  }

  function icon(path) {
    var svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 24 24");
    svg.setAttribute("aria-hidden", "true");
    var p = document.createElementNS("http://www.w3.org/2000/svg", "path");
    p.setAttribute("d", path);
    svg.appendChild(p);
    return svg;
  }

  function copyButton(text) {
    var button = el("button", { type: "button", class: "cw-copytext", text: t.copy });
    button.addEventListener("click", function () {
      navigator.clipboard.writeText(typeof text === "function" ? text() : text).then(function () {
        button.textContent = t.copied;
        setTimeout(function () { button.textContent = t.copy; }, 1400);
      });
    });
    return button;
  }

  function command(text) {
    var code = el("code", { text: typeof text === "function" ? text() : text });
    return el("div", { class: "cw-line" }, [code, copyButton(function () { return code.textContent; })]);
  }

  function gallery(root, entries, wizard) {
    var search = root.querySelector("input[type=search]");
    var tagBar = root.querySelector(".cw-starters__tags");
    var grid = root.querySelector(".cw-starters__grid");
    var empty = el("p", { class: "cw-starters__empty", text: t.none, hidden: "" });
    var tag = null;
    root.appendChild(empty);

    var tags = [];
    entries.forEach(function (e) { e.tags.forEach(function (x) { if (tags.indexOf(x) < 0) { tags.push(x); } }); });
    tags.sort().forEach(function (x) {
      var b = el("button", { type: "button", "aria-pressed": "false", text: "#" + x });
      b.addEventListener("click", function () {
        tag = tag === x ? null : x;
        tagBar.querySelectorAll("button").forEach(function (o) { o.setAttribute("aria-pressed", String(o === b && tag === x)); });
        filter();
      });
      tagBar.appendChild(b);
    });

    var cards = entries.map(function (e) {
      var text = de && e.de ? e.de : e;
      var use = el("button", { type: "button", class: "cw-btn", text: t.wizard });
      use.addEventListener("click", function () { wizard.use(e.id); });
      var card = el("article", { class: "cw-spec", id: "spec-" + e.id }, [
        el("div", { class: "cw-spec__head" }, [icon(e.icon), el("h3", { text: text.title })]),
        el("p", { text: text.description }),
        el("p", { class: "cw-spec__tags", text: e.tags.map(function (x) { return "#" + x; }).join("  ") }),
        command("coworkee new MyApp --spec " + e.id + ".json"),
        el("div", { class: "cw-spec__actions" }, [
          el("a", { class: "cw-btn cw-btn--primary", href: new URL("specs/" + e.id + ".json", assets).href, download: e.id + ".json", text: t.download }),
          use
        ])
      ]);
      card.dataset.search = [e.id, e.title, e.description, text.title, text.description].concat(e.tags).join(" ").toLowerCase();
      card.dataset.tags = e.tags.join(" ");
      grid.appendChild(card);
      return card;
    });

    function filter() {
      var q = search.value.trim().toLowerCase();
      var shown = 0;
      cards.forEach(function (c) {
        var on = (!tag || c.dataset.tags.split(" ").indexOf(tag) >= 0) && (!q || c.dataset.search.indexOf(q) >= 0);
        c.hidden = !on;
        shown += on ? 1 : 0;
      });
      empty.hidden = shown > 0;
    }

    search.addEventListener("input", filter);
  }

  // the keywords coworkee spec schema uses: type, enum, properties, additionalProperties, items, pattern, required
  function validate(schema, value, path, problems) {
    var types = [].concat(schema.type || []);
    var actual = value === null ? "null" : Array.isArray(value) ? "array" : Number.isInteger(value) ? "integer" : typeof value;
    if (types.length && types.indexOf(actual) < 0 && !(actual === "integer" && types.indexOf("number") >= 0)) {
      problems.push(path + ": " + actual + " instead of " + types.join(" or "));
      return;
    }
    if (schema.enum && schema.enum.indexOf(value) < 0) {
      problems.push(path + ": '" + value + "' is not one of " + schema.enum.filter(Boolean).slice(0, 8).join(", ") + (schema.enum.length > 8 ? ", …" : ""));
    }
    if (schema.pattern && typeof value === "string" && !new RegExp(schema.pattern).test(value)) {
      problems.push(path + ": '" + value + "' does not match " + schema.pattern);
    }
    if (actual === "object") {
      (schema.required || []).forEach(function (k) { if (!(k in value)) { problems.push(path + "." + k + ": missing"); } });
      Object.keys(value).forEach(function (k) {
        var inner = schema.properties && schema.properties[k];
        if (inner) { validate(inner, value[k], path + "." + k, problems); } else if (schema.additionalProperties === false) { problems.push(path + "." + k + ": unknown"); }
      });
    }
    if (actual === "array" && schema.items) {
      value.forEach(function (v, i) { validate(schema.items, v, path + "[" + i + "]", problems); });
    }
    return problems;
  }

  function wizard(form, entries, schema) {
    var options = schema.properties.options.properties;
    var out = document.getElementById("cw-wizard-json");
    var cmd = document.getElementById("cw-wizard-command");
    var problems = document.getElementById("cw-wizard-problems");
    var field = function (name) { return form.elements[name]; };
    var base = null;

    var starter = field("starter");
    starter.appendChild(el("option", { value: "", text: t.blank }));
    entries.forEach(function (e) { starter.appendChild(el("option", { value: e.id, text: de && e.de ? e.de.title : e.title })); });

    var ai = field("ai");
    ai.appendChild(el("option", { value: "off", text: t.off }));
    options.aiProvider.enum.forEach(function (p) { ai.appendChild(el("option", { value: p, text: p })); });

    function setForm(s) {
      var o = s.options || {};
      var pick = function (id) { return id in o ? o[id] : options[id].default; };
      field("name").value = s.name || "MyApp";
      field("title").value = (s.branding && s.branding.title) || "";
      field("database").value = pick("database");
      ["redis", "keycloak", "reports"].forEach(function (id) { field(id).checked = pick(id) === true; });
      ai.value = pick("ai") === false ? "off" : pick("aiProvider");
      var languages = o.languages || ["en", "de"];
      form.querySelectorAll("input[name=languages]").forEach(function (c) { c.checked = languages.indexOf(c.value) >= 0; });
    }

    function build() {
      var s = JSON.parse(JSON.stringify(base || {}));
      var o = s.options || {};
      var set = function (id, v) { if (v === options[id].default) { delete o[id]; } else { o[id] = v; } };
      set("database", field("database").value);
      ["redis", "keycloak", "reports"].forEach(function (id) { set(id, field(id).checked); });
      if (ai.value === "off") { o.ai = false; delete o.aiProvider; } else { set("ai", true); set("aiProvider", ai.value); }
      if (o.database !== undefined && o.database !== "postgres" && o.backup === true) { delete o.backup; }
      var languages = Array.prototype.filter.call(form.querySelectorAll("input[name=languages]"), function (c) { return c.checked; }).map(function (c) { return c.value; });
      if (languages.join() === "en,de") { delete o.languages; } else { o.languages = languages; }
      if (languages.length && languages.indexOf(o.defaultLanguage || "en") < 0) { o.defaultLanguage = languages[0]; }

      var title = field("title").value.trim();
      var branding = s.branding || {};
      if (title) { branding.title = title; } else { delete branding.title; }

      var result = { name: field("name").value.trim() };
      if (Object.keys(o).length) { result.options = o; }
      if (Object.keys(branding).length) { result.branding = branding; }
      Object.keys(s).forEach(function (k) { if (!(k in result) && k !== "options" && k !== "branding") { result[k] = s[k]; } });

      var found = validate(schema, result, "spec", []);
      if (!/^[A-Za-z][A-Za-z0-9]*$/.test(result.name)) { found.unshift("name: " + t.nameRule); }
      if (!languages.length) { found.push("languages: " + t.noLanguage); }
      return { spec: result, problems: found };
    }

    function file() { return (field("name").value.trim() || "app").toLowerCase() + ".json"; }

    function render() {
      var r = build();
      out.textContent = JSON.stringify(r.spec, null, 2);
      cmd.querySelector("code").textContent = "coworkee new " + (r.spec.name || "MyApp") + " --spec " + file();
      problems.replaceChildren.apply(problems, r.problems.length ? r.problems.map(function (p) { return el("li", { text: p }); }) : [el("li", { class: "cw-ok", text: t.valid })]);
      problems.classList.toggle("cw-problems--bad", r.problems.length > 0);
    }

    function use(id) {
      starter.value = id;
      (id ? spec(id) : Promise.resolve(null)).then(function (s) {
        base = s;
        setForm(s || {});
        render();
      });
    }

    starter.addEventListener("change", function () { use(starter.value); });
    form.addEventListener("input", function (e) { if (e.target !== starter) { render(); } });
    form.addEventListener("submit", function (e) { e.preventDefault(); });
    cmd.appendChild(copyButton(function () { return cmd.querySelector("code").textContent; }));
    document.getElementById("cw-wizard-copy").replaceWith(copyButton(function () { return out.textContent; }));
    document.getElementById("cw-wizard-download").addEventListener("click", function () {
      var a = el("a", { href: URL.createObjectURL(new Blob([out.textContent + "\n"], { type: "application/json" })), download: file() });
      document.body.appendChild(a);
      a.click();
      a.remove();
    });
    use("");
    return {
      use: function (id) {
        use(id);
        form.scrollIntoView({ behavior: "smooth", block: "start" });
      }
    };
  }

  var root = document.getElementById("cw-starters");
  var form = document.getElementById("cw-wizard");
  if (!root || !form) { return; }
  Promise.all([load("specs/gallery.json"), load("spec.schema.json")]).then(function (r) {
    gallery(root, r[0], wizard(form, r[0], r[1]));
  }).catch(function () {
    root.appendChild(el("p", { class: "cw-starters__empty", text: t.failed }));
  });
})();
