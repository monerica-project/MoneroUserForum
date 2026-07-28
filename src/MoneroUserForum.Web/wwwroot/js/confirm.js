// Global confirm handler for forms carrying data-confirm="...". Replaces inline
// onsubmit="confirm(...)" so the site can run under a strict CSP with no inline JS.
(function () {
  document.addEventListener('submit', function (e) {
    var form = e.target;
    var msg = form && form.getAttribute ? form.getAttribute('data-confirm') : null;
    if (msg && !window.confirm(msg)) { e.preventDefault(); }
  }, true);
})();
