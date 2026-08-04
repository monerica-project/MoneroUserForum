// Lightweight markdown toolbar — progressive enhancement over a plain <textarea>.
// Without JS the textarea still works (members can type markdown by hand).
(function () {
  function fireInput(ta) { ta.dispatchEvent(new Event('input', { bubbles: true })); }

  function surround(ta, before, after, placeholder) {
    var s = ta.selectionStart, e = ta.selectionEnd;
    var sel = ta.value.substring(s, e) || (placeholder || '');
    ta.value = ta.value.substring(0, s) + before + sel + after + ta.value.substring(e);
    ta.focus();
    var pos = s + before.length;
    ta.selectionStart = pos;
    ta.selectionEnd = pos + sel.length;
    fireInput(ta);
  }

  function makeLink(ta) {
    var s = ta.selectionStart, e = ta.selectionEnd;
    var sel = ta.value.substring(s, e);
    var url = window.prompt('Link URL', 'https://');
    if (!url) { return; }
    var label = sel || window.prompt('Link text', url) || url;
    var md = '[' + label + '](' + url + ')';
    ta.value = ta.value.substring(0, s) + md + ta.value.substring(e);
    ta.focus();
    ta.selectionStart = ta.selectionEnd = s + md.length;
    fireInput(ta);
  }

  function btn(label, title, fn) {
    var b = document.createElement('button');
    b.type = 'button';
    b.textContent = label;
    b.title = title;
    b.addEventListener('click', function (ev) { ev.preventDefault(); fn(); });
    return b;
  }

  function csrfToken(ta) {
    var form = ta.closest ? ta.closest('form') : null;
    var el = form && form.querySelector('input[name="__RequestVerificationToken"]');
    return el ? el.value : '';
  }

  function attach(ta) {
    if (ta.dataset.mdReady) { return; }
    ta.dataset.mdReady = '1';

    var bar = document.createElement('div');
    bar.className = 'md-toolbar';
    bar.appendChild(btn('B', 'Bold', function () { surround(ta, '**', '**', 'bold'); }));
    bar.appendChild(btn('I', 'Italic', function () { surround(ta, '*', '*', 'italic'); }));
    bar.appendChild(btn('🔗 Link', 'Select text, then click to turn it into a link', function () { makeLink(ta); }));
    bar.appendChild(btn('❝ Quote', 'Quote', function () { surround(ta, '\n> ', '', 'quote'); }));
    bar.appendChild(btn('</> Code', 'Inline code', function () { surround(ta, '`', '`', 'code'); }));

    // Preview pane — rendered by the server through the real markdown + sanitizer
    // pipeline, so what you see is exactly what will be posted.
    var pane = document.createElement('div');
    pane.className = 'md-preview post-md';
    pane.style.display = 'none';

    var showing = false;
    var previewBtn = btn('👁 Preview', 'See how your post will look before submitting', function () {
      if (showing) {                       // toggle back to editing
        pane.style.display = 'none';
        ta.style.display = '';
        ta.focus();
        previewBtn.textContent = '👁 Preview';
        showing = false;
        return;
      }
      if (!ta.value.trim()) { ta.focus(); return; }

      var fd = new FormData();
      fd.append('body', ta.value);
      var token = csrfToken(ta);
      if (token) { fd.append('__RequestVerificationToken', token); }

      pane.innerHTML = '<p class="md-hint" style="margin:0;">Loading preview…</p>';
      pane.style.display = '';
      ta.style.display = 'none';
      previewBtn.textContent = '✏️ Edit';
      showing = true;

      fetch('/thread/preview', { method: 'POST', body: fd, headers: { 'X-Requested-With': 'fetch' } })
        .then(function (r) { return r.ok ? r.text() : null; })
        .then(function (html) {
          // Server output is already sanitized by the same pipeline used for real posts.
          pane.innerHTML = (html && html.trim()) ? html : '<p class="md-hint" style="margin:0;">Nothing to preview yet.</p>';
        })
        .catch(function () { pane.innerHTML = '<p class="md-hint" style="margin:0;">Preview unavailable — try again.</p>'; });
    });
    bar.appendChild(previewBtn);

    ta.parentNode.insertBefore(bar, ta);
    if (ta.nextSibling) { ta.parentNode.insertBefore(pane, ta.nextSibling); }
    else { ta.parentNode.appendChild(pane); }

    var hint = document.createElement('div');
    hint.className = 'md-hint';
    hint.textContent = 'Highlight text and click 🔗 to make it a link. Also: **bold**, *italic*, [text](https://…). Use 👁 Preview to check it first.';
    if (pane.nextSibling) { ta.parentNode.insertBefore(hint, pane.nextSibling); }
    else { ta.parentNode.appendChild(hint); }
  }

  document.addEventListener('DOMContentLoaded', function () {
    var list = document.querySelectorAll('textarea.md-editor');
    for (var i = 0; i < list.length; i++) { attach(list[i]); }
  });
})();
