// SnapForge website: mobile menu and "copy checksum" button. No network access, no storage, no tracking.
(function () {
  "use strict";
  document.documentElement.classList.add("js");

  function ready(fn) {
    if (document.readyState !== "loading") fn();
    else document.addEventListener("DOMContentLoaded", fn);
  }

  ready(function () {
    var toggle = document.querySelector(".nav-toggle");
    var nav = document.getElementById("site-nav");
    if (toggle && nav) {
      toggle.hidden = false;
      var close = function (focusToggle) {
        toggle.setAttribute("aria-expanded", "false");
        nav.classList.remove("open");
        if (focusToggle) toggle.focus();
      };
      toggle.addEventListener("click", function () {
        var open = toggle.getAttribute("aria-expanded") === "true";
        toggle.setAttribute("aria-expanded", open ? "false" : "true");
        nav.classList.toggle("open", !open);
      });
      document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && toggle.getAttribute("aria-expanded") === "true") close(true);
      });
    }

    var buttons = document.querySelectorAll("button.copy[data-copy]");
    Array.prototype.forEach.call(buttons, function (btn) {
      var target = document.getElementById(btn.getAttribute("data-copy"));
      if (!target || !navigator.clipboard) { btn.hidden = true; return; }
      var status = document.createElement("span");
      status.className = "visually-hidden";
      status.setAttribute("role", "status");
      btn.after(status);
      btn.addEventListener("click", function () {
        navigator.clipboard.writeText(target.textContent.trim()).then(function () {
          btn.textContent = "Copied";
          status.textContent = "Checksum copied to the clipboard";
          setTimeout(function () { btn.textContent = "Copy"; status.textContent = ""; }, 2000);
        }, function () {
          status.textContent = "Copy failed; select the checksum and copy it manually";
        });
      });
    });
  });
})();
