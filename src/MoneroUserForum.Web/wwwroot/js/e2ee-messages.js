/*
 * Private-message encryption glue.
 *  - Conversation page: decrypts each message bubble in-browser, encrypts replies to
 *    both the recipient AND yourself before posting, polls for new messages, and warns
 *    if the other person's keys change (trust-on-first-use).
 *  - Compose page: encrypts the first message before it's sent.
 * The server only ever sees ciphertext envelopes. Requires WebCrypto + unlocked keys
 * (which login provides); degrades to a clear "log in again" message otherwise.
 */
(function () {
    'use strict';
    if (!window.E2EE || !E2EE.supported()) { return; }

    var selfP = E2EE.loadSelf();

    function tokenOf(form) {
        var i = form && form.querySelector('input[name="__RequestVerificationToken"]');
        return i ? i.value : '';
    }
    function setHidden(form, name, val) {
        var i = form.querySelector('input[name="' + name + '"]');
        if (!i) { i = document.createElement('input'); i.type = 'hidden'; i.name = name; form.appendChild(i); }
        i.value = val;
    }
    function fetchPubKey(name) {
        return fetch('/messages/pubkey/' + encodeURIComponent(name), { headers: { 'X-Requested-With': 'fetch' } })
            .then(function (r) { if (!r.ok) { throw new Error('no-keys'); } return r.json(); });
    }

    var log = document.getElementById('dm-log');
    if (log) { initConversation(log); } else { initCompose(); }

    // ---- conversation ----
    function initConversation(log) {
        var partnerName = log.getAttribute('data-partner');
        var toInput = document.querySelector('form input[name="ToUsername"]');
        var replyForm = toInput ? toInput.closest('form') : null;
        var ctx = null, warned = false;

        function ensureCtx() {
            if (ctx) { return Promise.resolve(ctx); }
            return Promise.all([selfP, fetchPubKey(partnerName)]).then(function (r) {
                var me = r[0], pk = r[1];
                if (!me || !me.encPriv) { throw new Error('locked'); }
                return Promise.all([
                    E2EE.importSignPub(me.signPub), E2EE.importEncPub(me.encPub),
                    E2EE.importEncPub(pk.encPublicKey), E2EE.importSignPub(pk.signPublicKey),
                    E2EE.rememberPeer(pk.userId, pk.encPublicKey, pk.signPublicKey)
                ]).then(function (x) {
                    ctx = { me: me, mySignPub: x[0], myEncPub: x[1], partner: { id: pk.userId, encPub: x[2], signPub: x[3] } };
                    if (x[4] && x[4].changed && !warned) { warned = true; showKeyChangeWarning(partnerName); }
                    return ctx;
                });
            });
        }

        function decryptAll() {
            var nodes = log.querySelectorAll('.dm-enc[data-envelope]');
            if (!nodes.length) { return; }
            ensureCtx().then(function (c) {
                nodes.forEach(function (node) {
                    var envJson = node.getAttribute('data-envelope');
                    node.removeAttribute('data-envelope');
                    var env;
                    try { env = JSON.parse(envJson); } catch (e) { node.textContent = '🔒 (unreadable)'; return; }
                    var signPub = (String(env.from) === String(c.me.userId)) ? c.mySignPub : c.partner.signPub;
                    E2EE.decryptMessage(envJson, { myId: c.me.userId, encPriv: c.me.encPriv, senderSignPub: signPub })
                        .then(function (pt) { node.textContent = pt; })
                        .catch(function () { node.textContent = '🔒 (unable to decrypt)'; });
                });
            }).catch(function (e) {
                nodes.forEach(function (node) {
                    node.textContent = (e && e.message === 'locked') ? '🔒 Log in again to read' : '🔒 (unavailable)';
                });
            });
        }

        function toBottom() { log.scrollTop = log.scrollHeight; }
        decryptAll(); toBottom();

        function poll() {
            fetch('/messages/feed/' + encodeURIComponent(partnerName), { headers: { 'X-Requested-With': 'fetch' } })
                .then(function (r) { return r.ok ? r.text() : null; })
                .then(function (html) {
                    if (html === null) { return; }
                    var nearBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 60;
                    log.innerHTML = html;
                    decryptAll();
                    if (nearBottom) { toBottom(); }
                }).catch(function () {});
        }
        setInterval(poll, 5000);

        if (replyForm) {
            var ta = replyForm.querySelector('textarea[name="Body"]');
            if (ta) { ta.removeAttribute('required'); }
            replyForm.addEventListener('submit', function (ev) {
                ev.preventDefault();
                var text = ta ? ta.value.trim() : '';
                if (!text) { return; }
                ensureCtx().then(function (c) {
                    return E2EE.encryptMessage(text, {
                        from: c.me.userId, to: c.partner.id,
                        parties: [{ id: c.partner.id, encPub: c.partner.encPub }, { id: c.me.userId, encPub: c.myEncPub }],
                        signPriv: c.me.signPriv
                    });
                }).then(function (envelope) {
                    var fd = new FormData();
                    fd.append('__RequestVerificationToken', tokenOf(replyForm));
                    fd.append('ToUsername', partnerName);
                    fd.append('Envelope', envelope);
                    return fetch('/messages/send', { method: 'POST', body: fd, headers: { 'X-Requested-With': 'fetch' } });
                }).then(function () { if (ta) { ta.value = ''; } poll(); })
                    .catch(function (e) {
                        window.alert(e && e.message === 'locked'
                            ? 'Log in again to unlock your encryption keys.'
                            : 'Could not send the message.');
                    });
            });
        }
    }

    // ---- compose (first message) ----
    function initCompose() {
        var toInput = document.querySelector('form input[name="ToUsername"]');
        var form = toInput ? toInput.closest('form') : null;
        if (!form) { return; }
        var ta = form.querySelector('textarea[name="Body"]');
        if (ta) { ta.removeAttribute('required'); }
        var submitting = false;

        form.addEventListener('submit', function (ev) {
            if (submitting) { return; }
            var text = ta ? ta.value.trim() : '';
            var to = toInput ? toInput.value.trim() : '';
            if (!text || !to) { return; } // let native/server validation handle empties
            ev.preventDefault();
            Promise.all([selfP, fetchPubKey(to)]).then(function (r) {
                var me = r[0], pk = r[1];
                if (!me || !me.encPriv) { throw new Error('locked'); }
                return Promise.all([E2EE.importEncPub(me.encPub), E2EE.importEncPub(pk.encPublicKey)]).then(function (k) {
                    return E2EE.encryptMessage(text, {
                        from: me.userId, to: pk.userId,
                        parties: [{ id: pk.userId, encPub: k[1] }, { id: me.userId, encPub: k[0] }],
                        signPriv: me.signPriv
                    });
                });
            }).then(function (envelope) {
                setHidden(form, 'Envelope', envelope);
                if (ta) { ta.value = ''; } // plaintext never leaves the browser
                submitting = true;
                form.submit();
            }).catch(function (e) {
                window.alert(e && e.message === 'no-keys' ? 'That member hasn’t set up encryption yet, so you can’t message them.'
                    : e && e.message === 'locked' ? 'Log in again to unlock your encryption keys.'
                    : 'Could not encrypt the message.');
            });
        });
    }

    function showKeyChangeWarning(name) {
        var d = document.createElement('div');
        d.setAttribute('style', 'background:#5a1e1e;color:#ffe;border:1px solid #a33;padding:8px 12px;border-radius:8px;margin-bottom:8px;');
        d.textContent = '⚠ ' + name + '’s encryption keys have changed since you last talked. '
            + 'If you didn’t expect this, check with them before trusting new messages.';
        var log = document.getElementById('dm-log');
        if (log && log.parentNode) { log.parentNode.insertBefore(d, log); }
    }
})();
