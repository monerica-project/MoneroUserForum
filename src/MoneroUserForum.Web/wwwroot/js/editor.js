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
    ta.parentNode.insertBefore(bar, ta);

    var hint = document.createElement('div');
    hint.className = 'md-hint';
    hint.textContent = 'Highlight text and click 🔗 to make it a link. Also: **bold**, *italic*, [text](https://…).';
    if (ta.nextSibling) { ta.parentNode.insertBefore(hint, ta.nextSibling); }
    else { ta.parentNode.appendChild(hint); }
  }

  document.addEventListener('DOMContentLoaded', function () {
    var list = document.querySelectorAll('textarea.md-editor');
    for (var i = 0; i < list.length; i++) { attach(list[i]); }
  });
})();
