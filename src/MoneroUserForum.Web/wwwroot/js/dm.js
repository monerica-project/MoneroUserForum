// Conversation page: poll for new direct messages so they appear without a
// reload. External file so a strict CSP (script-src 'self') applies.
(function () {
  var log = document.getElementById('dm-log');
  if (!log) { return; }
  var partner = log.getAttribute('data-partner');
  function toBottom() { log.scrollTop = log.scrollHeight; }
  toBottom();
  function poll() {
    fetch('/messages/feed/' + encodeURIComponent(partner), { headers: { 'X-Requested-With': 'fetch' } })
      .then(function (r) { return r.ok ? r.text() : null; })
      .then(function (html) {
        if (html === null) { return; }
        var nearBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 60;
        log.innerHTML = html;
        if (nearBottom) { toBottom(); }
      })
      .catch(function () {});
  }
  setInterval(poll, 5000);
})();
