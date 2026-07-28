// Compose page: username type-ahead for the "To" field. External file so a
// strict Content-Security-Policy (script-src 'self') can block injected scripts.
(function () {
  var input = document.getElementById('to-user');
  var box = document.getElementById('to-suggest');
  var body = document.getElementById('body-input');
  if (!input || !box) { return; }

  if (input.value.trim().length > 0) { if (body) { body.focus(); } } else { input.focus(); }

  var timer, last = null;
  function hide() { box.style.display = 'none'; box.innerHTML = ''; }
  function render(items) {
    box.innerHTML = '';
    if (!items || !items.length) { hide(); return; }
    items.forEach(function (it) {
      var a = document.createElement('a');
      a.href = '#';
      a.textContent = it.name;
      a.addEventListener('click', function (e) { e.preventDefault(); input.value = it.name; hide(); if (body) { body.focus(); } });
      box.appendChild(a);
    });
    box.style.display = 'block';
  }
  input.addEventListener('input', function () {
    var q = input.value.trim();
    if (q === last) { return; }
    last = q;
    clearTimeout(timer);
    if (q.length < 1) { hide(); return; }
    timer = setTimeout(function () {
      fetch('/messages/suggest-users?q=' + encodeURIComponent(q), { headers: { 'X-Requested-With': 'fetch' } })
        .then(function (r) { return r.ok ? r.json() : []; })
        .then(render).catch(hide);
    }, 160);
  });
  document.addEventListener('click', function (e) { if (e.target !== input && !box.contains(e.target)) { hide(); } });
})();
