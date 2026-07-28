// Nav search type-ahead: shows matching threads as you type. Without JS the box
// still submits to the full search page.
(function () {
  var form = document.querySelector('form.nav-search');
  if (!form) { return; }
  var input = form.querySelector('input[name="q"]');
  if (!input) { return; }

  var box = document.createElement('div');
  box.className = 'search-suggest';
  box.style.display = 'none';
  form.appendChild(box);

  var timer, lastQ = null;

  function hide() { box.style.display = 'none'; box.innerHTML = ''; }

  function render(items) {
    box.innerHTML = '';
    if (!items || !items.length) { hide(); return; }
    items.forEach(function (it) {
      var a = document.createElement('a');
      a.href = '/thread/' + it.slug;
      a.textContent = it.title;
      box.appendChild(a);
    });
    box.style.display = 'block';
  }

  function go(q) {
    fetch('/search/suggest?q=' + encodeURIComponent(q), { headers: { 'X-Requested-With': 'fetch' } })
      .then(function (r) { return r.ok ? r.json() : []; })
      .then(render)
      .catch(hide);
  }

  input.addEventListener('input', function () {
    var q = input.value.trim();
    if (q === lastQ) { return; }
    lastQ = q;
    clearTimeout(timer);
    if (q.length < 2) { hide(); return; }
    timer = setTimeout(function () { go(q); }, 180);
  });

  input.addEventListener('focus', function () { if (box.childNodes.length) { box.style.display = 'block'; } });
  document.addEventListener('click', function (e) { if (!form.contains(e.target)) { hide(); } });
})();
