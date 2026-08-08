// Lightweight markdown toolbar — progressive enhancement over a plain <textarea>.
// Without JS the textarea still works (members can type markdown by hand).
(function () {
  function fireInput(ta) { ta.dispatchEvent(new Event('input', { bubbles: true })); }

  // Auto-grow the textarea with its content, up to ~the full viewport height,
  // then scroll. Progressive enhancement — without JS the textarea keeps its rows.
  function autoGrow(ta) {
    var minH = ta.offsetHeight || 120;   // the rows-based starting height
    function resize() {
      var maxH = Math.max(160, Math.floor(window.innerHeight * 0.9));
      ta.style.height = 'auto';
      var h = Math.min(Math.max(ta.scrollHeight, minH), maxH);
      ta.style.height = h + 'px';
      ta.style.overflowY = ta.scrollHeight > maxH ? 'auto' : 'hidden';
    }
    ta.addEventListener('input', resize);
    window.addEventListener('resize', resize);
    resize();
  }

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

  // One clear dialog with BOTH the link text and the URL, plus a live preview,
  // so the writer can see exactly how the link will look before inserting it.
  function makeLink(ta) {
    var s = ta.selectionStart, e = ta.selectionEnd;
    var sel = ta.value.substring(s, e);

    var overlay = document.createElement('div');
    overlay.className = 'md-modal';
    var panel = document.createElement('div');
    panel.className = 'md-modal-panel';
    overlay.appendChild(panel);

    var title = document.createElement('h3');
    title.textContent = 'Insert a link';
    panel.appendChild(title);

    function field(labelText, value, placeholder, type) {
      var wrap = document.createElement('div');
      wrap.className = 'md-modal-field';
      var lab = document.createElement('label');
      lab.textContent = labelText;
      var inp = document.createElement('input');
      inp.type = type || 'text';
      inp.value = value;
      inp.placeholder = placeholder;
      inp.setAttribute('aria-label', labelText);
      wrap.appendChild(lab);
      wrap.appendChild(inp);
      panel.appendChild(wrap);
      return inp;
    }

    var textInput = field('Link text (what readers see)', sel || '', 'e.g. Monero project', 'text');
    var urlInput = field('Link address (URL)', '', 'https://example.com', 'url');

    var preview = document.createElement('div');
    preview.className = 'md-modal-preview';
    panel.appendChild(preview);

    var previewLabel = document.createElement('span');
    previewLabel.textContent = 'Preview: ';
    var previewLink = document.createElement('span');
    previewLink.className = 'md-modal-previewlink';
    var previewCode = document.createElement('div');
    previewCode.className = 'md-modal-previewcode';
    preview.appendChild(previewLabel);
    preview.appendChild(previewLink);
    preview.appendChild(previewCode);

    function refresh() {
      var t = textInput.value.trim();
      var u = urlInput.value.trim();
      var shown = t || u || 'link text';
      previewLink.textContent = shown;
      previewCode.textContent = '[' + shown + '](' + (u || 'https://…') + ')';
    }
    textInput.addEventListener('input', refresh);
    urlInput.addEventListener('input', refresh);
    refresh();

    var actions = document.createElement('div');
    actions.className = 'md-modal-actions';
    var cancelBtn = document.createElement('button');
    cancelBtn.type = 'button';
    cancelBtn.className = 'btn';
    cancelBtn.textContent = 'Cancel';
    var insertBtn = document.createElement('button');
    insertBtn.type = 'button';
    insertBtn.className = 'btn btn-primary';
    insertBtn.textContent = 'Insert link';
    actions.appendChild(cancelBtn);
    actions.appendChild(insertBtn);
    panel.appendChild(actions);

    function close() {
      document.removeEventListener('keydown', onKey);
      if (overlay.parentNode) { overlay.parentNode.removeChild(overlay); }
      ta.focus();
    }
    function insert() {
      var label = textInput.value.trim();
      var url = urlInput.value.trim();
      if (!url || url === 'https://') { urlInput.focus(); return; }
      if (!label) { label = url; }
      var md = '[' + label + '](' + url + ')';
      ta.value = ta.value.substring(0, s) + md + ta.value.substring(e);
      ta.selectionStart = ta.selectionEnd = s + md.length;
      fireInput(ta);
      close();
    }
    function onKey(ev) {
      if (ev.key === 'Escape') { close(); }
      else if (ev.key === 'Enter' && (ev.target === textInput || ev.target === urlInput)) { ev.preventDefault(); insert(); }
    }
    cancelBtn.addEventListener('click', close);
    insertBtn.addEventListener('click', insert);
    overlay.addEventListener('click', function (ev) { if (ev.target === overlay) { close(); } });
    document.addEventListener('keydown', onKey);

    document.body.appendChild(overlay);
    // If text was highlighted it's already the link text — jump to the URL box.
    if (sel) { urlInput.focus(); urlInput.setSelectionRange(urlInput.value.length, urlInput.value.length); }
    else { textInput.focus(); }
  }

  // Insert a YouTube link on its own line so ForumMarkup renders it as a
  // click-to-play embed. One field, with a plain-language note about Tor/VPN.
  function insertYouTube(ta) {
    var s = ta.selectionStart, e = ta.selectionEnd;
    var overlay = document.createElement('div');
    overlay.className = 'md-modal';
    var panel = document.createElement('div');
    panel.className = 'md-modal-panel';
    overlay.appendChild(panel);

    var title = document.createElement('h3');
    title.textContent = 'Embed a YouTube video';
    panel.appendChild(title);

    var wrap = document.createElement('div');
    wrap.className = 'md-modal-field';
    var lab = document.createElement('label');
    lab.textContent = 'YouTube link';
    var inp = document.createElement('input');
    inp.type = 'url';
    inp.value = '';
    inp.placeholder = 'https://www.youtube.com/watch?v=…';
    inp.setAttribute('aria-label', 'YouTube link');
    wrap.appendChild(lab);
    wrap.appendChild(inp);
    panel.appendChild(wrap);

    var note = document.createElement('div');
    note.className = 'md-modal-preview';
    note.textContent = 'It becomes a click-to-play video in your post. Nothing loads from YouTube until a reader clicks it, so the page still works on Tor/VPN — with a fallback link to open it on YouTube.';
    panel.appendChild(note);

    var actions = document.createElement('div');
    actions.className = 'md-modal-actions';
    var cancelBtn = document.createElement('button');
    cancelBtn.type = 'button'; cancelBtn.className = 'btn'; cancelBtn.textContent = 'Cancel';
    var okBtn = document.createElement('button');
    okBtn.type = 'button'; okBtn.className = 'btn btn-primary'; okBtn.textContent = 'Insert video';
    actions.appendChild(cancelBtn); actions.appendChild(okBtn);
    panel.appendChild(actions);

    function close() {
      document.removeEventListener('keydown', onKey);
      if (overlay.parentNode) { overlay.parentNode.removeChild(overlay); }
      ta.focus();
    }
    function ok() {
      var url = inp.value.trim();
      if (!/(youtube\.com|youtu\.be)/i.test(url)) { inp.focus(); return; }
      var before = ta.value.substring(0, s), after = ta.value.substring(e);
      var pre = before ? (/\n\n$/.test(before) ? '' : (/\n$/.test(before) ? '\n' : '\n\n')) : '';
      var post = after ? (/^\n\n/.test(after) ? '' : (/^\n/.test(after) ? '\n' : '\n\n')) : '\n';
      var ins = pre + url + post;
      ta.value = before + ins + after;
      ta.selectionStart = ta.selectionEnd = before.length + ins.length;
      fireInput(ta);
      close();
    }
    function onKey(ev) {
      if (ev.key === 'Escape') { close(); }
      else if (ev.key === 'Enter' && ev.target === inp) { ev.preventDefault(); ok(); }
    }
    cancelBtn.addEventListener('click', close);
    okBtn.addEventListener('click', ok);
    overlay.addEventListener('click', function (ev) { if (ev.target === overlay) { close(); } });
    document.addEventListener('keydown', onKey);
    document.body.appendChild(overlay);
    inp.focus(); inp.setSelectionRange(inp.value.length, inp.value.length);
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

    autoGrow(ta);

    var bar = document.createElement('div');
    bar.className = 'md-toolbar';
    bar.appendChild(btn('B', 'Bold', function () { surround(ta, '**', '**', 'bold'); }));
    bar.appendChild(btn('I', 'Italic', function () { surround(ta, '*', '*', 'italic'); }));
    bar.appendChild(btn('🔗 Link', 'Select text, then click to turn it into a link', function () { makeLink(ta); }));
    bar.appendChild(btn('▶ YouTube', 'Embed a YouTube video (click-to-play)', function () { insertYouTube(ta); }));
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
    // Put Preview right next to the Submit button — more discoverable than in the toolbar.
    previewBtn.classList.add('btn');
    previewBtn.style.marginRight = '8px';
    var form = ta.closest ? ta.closest('form') : null;
    var submitBtn = form && form.querySelector('button[type="submit"], input[type="submit"]');
    if (submitBtn && submitBtn.parentNode) {
      submitBtn.parentNode.insertBefore(previewBtn, submitBtn);
    } else {
      bar.appendChild(previewBtn);
    }

    ta.parentNode.insertBefore(bar, ta);
    if (ta.nextSibling) { ta.parentNode.insertBefore(pane, ta.nextSibling); }
    else { ta.parentNode.appendChild(pane); }
    // The formatting hint is server-rendered (_FormattingHelp) so it works without JS too.
  }

  document.addEventListener('DOMContentLoaded', function () {
    var list = document.querySelectorAll('textarea.md-editor');
    for (var i = 0; i < list.length; i++) { attach(list[i]); }
  });
})();
