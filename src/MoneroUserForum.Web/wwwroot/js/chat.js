// Global chatroom. Real-time via SignalR: the server sends a "chatUpdated" nudge
// when someone posts, and we pull the safe, server-rendered feed in response.
// SignalR auto-negotiates WebSockets -> SSE -> Long Polling, so it works over Tor
// and behind proxies that don't upgrade WebSocket connections. If SignalR can't
// connect at all, a plain interval poll keeps the room fresh. Without JS the form
// still submits normally (full page reload).
(function () {
  var log = document.getElementById('chat-log');
  if (!log) { return; }

  function toBottom() { log.scrollTop = log.scrollHeight; }
  toBottom();

  var polling = false;
  function poll(after) {
    if (polling) { return; }
    polling = true;
    fetch('/chat/feed', { headers: { 'X-Requested-With': 'fetch' } })
      .then(function (r) { return r.ok ? r.text() : null; })
      .then(function (html) {
        if (html !== null) {
          var nearBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 60;
          log.innerHTML = html;
          if (nearBottom) { toBottom(); }
        }
        if (after) { after(); }
      })
      .catch(function () {})
      .then(function () { polling = false; });
  }

  // ---- Real-time via SignalR (with graceful fallback) ----------------------
  var hubConnected = false;
  if (window.signalR) {
    var conn = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/chat')
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build();

    conn.on('chatUpdated', function () { poll(); });

    conn.onreconnected(function () { hubConnected = true; poll(); }); // catch up on missed messages
    conn.onreconnecting(function () { hubConnected = false; });
    conn.onclose(function () { hubConnected = false; });

    conn.start()
      .then(function () { hubConnected = true; })
      .catch(function () { hubConnected = false; });
  }

  // Fallback poll: runs every 5s only while the hub isn't connected. A slow 30s
  // safety poll always runs in case a nudge is ever missed.
  setInterval(function () { if (!hubConnected) { poll(); } }, 5000);
  setInterval(function () { poll(); }, 30000);

  // ---- Sending (AJAX so the cursor stays in the box) -----------------------
  var form = document.getElementById('chat-form');
  var input = document.getElementById('chat-input');
  if (form && input) {
    input.focus();
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      var body = input.value.trim();
      if (!body) { input.focus(); return; }

      fetch(form.getAttribute('action'), {
        method: 'POST',
        body: new FormData(form),
        headers: { 'X-Requested-With': 'fetch' }
      })
        .then(function () {
          input.value = '';
          input.focus();   // keep the cursor in the box
          poll(toBottom);  // show our own message right away
        })
        .catch(function () { input.focus(); });
    });
  }
})();
