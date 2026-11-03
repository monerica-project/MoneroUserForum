// Shows every forum time in the reader's own time zone. The server always stores and prints UTC;
// each time is a <time datetime="...Z"> tag. Nothing is sent back to the server, so the forum never
// learns anyone's time zone. Without JavaScript the UTC text from the server stays as it is.
//   <time class="ago">        -> "5m ago" recomputed from this device's clock (refreshed every minute),
//                                with the full local date/time on hover
//   <time class="local-time"> -> exact local date/time, e.g. "2026-10-30 20:54"
(function () {
  "use strict";

  function pad(n) { return n < 10 ? "0" + n : String(n); }

  function localStamp(d) {
    return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()) +
      " " + pad(d.getHours()) + ":" + pad(d.getMinutes());
  }

  function zoneName(d) {
    try {
      var part = new Intl.DateTimeFormat(undefined, { timeZoneName: "short" })
        .formatToParts(d).filter(function (p) { return p.type === "timeZoneName"; })[0];
      if (part) { return part.value; }
    } catch (e) { /* older browsers: fall through */ }
    var off = -d.getTimezoneOffset();
    return "UTC" + (off >= 0 ? "+" : "-") + pad(Math.floor(Math.abs(off) / 60)) + ":" + pad(Math.abs(off) % 60);
  }

  // Same wording as the server (ForumDisplay.TimeAgoText).
  function ago(d, now) {
    var s = (now - d.getTime()) / 1000;
    if (s < 60) { return "just now"; }
    var m = s / 60; if (m < 60) { return Math.floor(m) + "m ago"; }
    var h = m / 60; if (h < 24) { return Math.floor(h) + "h ago"; }
    var days = h / 24; if (days < 30) { return Math.floor(days) + "d ago"; }
    if (days < 365) { return Math.floor(days / 30) + "mo ago"; }
    return Math.floor(days / 365) + "y ago";
  }

  function parse(el) {
    var v = el.getAttribute("datetime");
    if (!v) { return null; }
    var d = new Date(v);
    return isNaN(d.getTime()) ? null : d;
  }

  function convert(root) {
    var now = Date.now();
    var scope = root && root.querySelectorAll ? root : document;
    var els = scope.querySelectorAll("time.ago, time.local-time");
    for (var i = 0; i < els.length; i++) {
      var el = els[i], d = parse(el);
      if (!d) { continue; }
      var full = localStamp(d) + " " + zoneName(d);
      if (el.classList.contains("ago")) {
        el.textContent = ago(d, now);
        el.title = full;
      } else {
        el.textContent = localStamp(d);
        el.title = "Your local time (" + zoneName(d) + ")";
      }
    }
  }

  function start() {
    convert(document);
    setInterval(function () { convert(document); }, 60000);
    // Chat and messages add new rows without a page load; convert those too.
    if (window.MutationObserver) {
      new MutationObserver(function (records) {
        for (var i = 0; i < records.length; i++) {
          var added = records[i].addedNodes;
          for (var j = 0; j < added.length; j++) {
            if (added[j].nodeType === 1) {
              if (added[j].matches && added[j].matches("time.ago, time.local-time")) { convert(added[j].parentNode); }
              else { convert(added[j]); }
            }
          }
        }
      }).observe(document.body, { childList: true, subtree: true });
    }
  }

  if (document.readyState === "loading") { document.addEventListener("DOMContentLoaded", start); }
  else { start(); }
})();
