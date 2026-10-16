// Click a screenshot to see it large: arrows and Esc work, the caption sits below. Used by the landing page and the screenshots page.
(function () {
  "use strict";
  var de = document.documentElement.lang === "de";
  var t = de ? { close: "Schließen", prev: "Zurück", next: "Weiter", label: "Screenshot" } : { close: "Close", prev: "Previous", next: "Next", label: "Screenshot" };
  var icons = {
    close: "M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z",
    prev: "M15.41 16.58 10.83 12l4.58-4.59L14 6l-6 6 6 6z",
    next: "M8.59 16.58 13.17 12 8.59 7.41 10 6l6 6-6 6z"
  };

  // figures of the screenshots page become links, so they work like the landing gallery
  document.querySelectorAll(".cw-shots figure").forEach(function (figure) {
    var img = figure.querySelector("img");
    var caption = figure.querySelector("figcaption");
    if (!img || img.closest("a")) { return; }
    var link = document.createElement("a");
    link.className = "cw-zoom";
    link.href = img.getAttribute("src");
    link.dataset.caption = caption ? caption.textContent.trim() : img.alt;
    img.replaceWith(link);
    link.appendChild(img);
  });
  if (!document.querySelector(".cw-zoom")) { return; }

  function button(kind) {
    var b = document.createElement("button");
    b.type = "button";
    b.className = "cw-lightbox__" + kind;
    b.setAttribute("aria-label", t[kind]);
    b.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="' + icons[kind] + '"/></svg>';
    return b;
  }

  var box = document.createElement("dialog");
  box.className = "cw-lightbox";
  box.setAttribute("aria-label", t.label);
  var img = document.createElement("img");
  img.alt = "";
  var text = document.createElement("p");
  var prev = button("prev"), next = button("next"), close = button("close");
  box.append(img, text, close, prev, next);
  document.body.appendChild(box);

  var items = [], index = 0;
  function show(i) {
    index = (i + items.length) % items.length;
    img.src = items[index].href;
    img.alt = text.textContent = items[index].dataset.caption || "";
  }

  document.addEventListener("click", function (e) {
    var link = e.target.closest(".cw-zoom");
    if (!link) { return; }
    e.preventDefault();
    var group = link.closest(".cw-gallery, .cw-shots, .cw-section") || document;
    items = Array.prototype.slice.call(group.querySelectorAll(".cw-zoom"));
    prev.hidden = next.hidden = items.length < 2;
    show(items.indexOf(link));
    box.showModal();
  });
  close.addEventListener("click", function () { box.close(); });
  prev.addEventListener("click", function () { show(index - 1); });
  next.addEventListener("click", function () { show(index + 1); });
  box.addEventListener("click", function (e) { if (e.target === box) { box.close(); } });
  box.addEventListener("keydown", function (e) {
    if (e.key === "ArrowLeft") { show(index - 1); }
    if (e.key === "ArrowRight") { show(index + 1); }
  });
})();
