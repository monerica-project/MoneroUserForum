/*
 * Private-message encryption glue.
 *  - Conversation page: decrypts each message bubble in-browser, encrypts replies to
 *    both the recipient AND yourself before posting, polls for new messages, and warns
 *    if the other person's keys change (trust-on-first-use).
 *  - Compose page: encrypts the first message before it's sent.
 * The server only ever sees ciphertext envelopes. Requires WebCrypto + unlocked keys.
 *
 * When the SENDER has no usable keys (an account created before E2EE that never
 * generated keys, or a fresh device where they're not unlocked), instead of failing
 * with a throwaway alert we surface a persistent on-page banner and an inline repair
 * step: generate-or-unlock the keys with the account password, then retry the send.
 */
(function () {
    'use strict';
    if (!window.E2EE || !E2EE.supported()) {
        // No WebCrypto/IndexedDB here (e.g. a private/Tor window, or a .onion over http). We can't
        // end-to-end encrypt — but the message should still send. We leave the plain <form> alone so
        // it posts the plaintext Body natively and the server stores it unencrypted. Just say so.
        var host = document.getElementById('dm-status');
        if (host) {
            showBannerInto(host,
                'Heads up: this browser can’t end-to-end encrypt messages (no WebCrypto available here), ' +
                'so anything you send is stored unencrypted. It will still be delivered. For encrypted ' +
                'messaging, open the forum in a normal https window of a browser with WebCrypto enabled.',
                false);
        }
        return;
    }

    var selfP = E2EE.loadSelf();

    // ---- small DOM/util helpers -----------------------------------------
    function tokenOf(form) {
        var i = (form || document).querySelector('input[name="__RequestVerificationToken"]');
        return i ? i.value : '';
    }
    function pageToken() {
        var i = document.querySelector('input[name="__RequestVerificationToken"]');
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
    function appendPayload(fd, p) {
        fd.append('EncPublicKey', p.encPublicKey);
        fd.append('SignPublicKey', p.signPublicKey);
        fd.append('KdfSalt', p.kdfSalt);
        fd.append('WrappedPrivateKeys', p.wrappedPrivateKeys);
        fd.append('RecoverySalt', p.recoverySalt);
        fd.append('RecoveryWrappedPrivateKeys', p.recoveryWrappedPrivateKeys);
    }

    // A persistent status banner shown near the top of the compose/conversation area.
    function statusHost() { return document.getElementById('dm-status'); }
    function showBanner(msg, isError) { showBannerInto(statusHost(), msg, isError); }
    function showBannerInto(host, msg, isError) {
        if (!host) { return null; }
        host.textContent = '';
        host.setAttribute('style',
            'display:block;padding:11px 14px;border-radius:8px;margin-bottom:12px;line-height:1.45;' +
            (isError
                ? 'background:#3a1414;border:1px solid #a33;color:#ffdede;'
                : 'background:#12261a;border:1px solid #2e6b45;color:#d7ffe6;'));
        host.textContent = msg;
        return host;
    }
    function clearBanner() { var h = statusHost(); if (h) { h.style.display = 'none'; h.textContent = ''; } }

    var log = document.getElementById('dm-log');
    if (log) { initConversation(log); } else { initCompose(); }

    // ---- key repair: generate (first time) or unlock (new device) --------
    // Resolves once `selfP` holds usable keys for this account. Renders its own
    // inline password prompt + (on first-time setup) a one-time recovery code.
    function repairKeys() {
        var host = statusHost();
        if (!host) { return Promise.reject(new Error('no-ui')); }
        return fetch('/account/e2ee-keys', { headers: { 'X-Requested-With': 'fetch' } })
            .then(function (r) { return r.json(); })
            .then(function (keys) {
                return promptPassword(host, keys.hasKeys).then(function (pass) {
                    if (keys.hasKeys) {
                        // Keys exist server-side; just unlock them onto this device.
                        return E2EE.unlockWithPassword(keys.userId, pass, keys)
                            .then(function () { selfP = E2EE.loadSelf(); return selfP; });
                    }
                    // No keys yet: generate in-browser, upload the public halves, show recovery code.
                    return E2EE.createIdentityPayload(keys.userId, pass).then(function (rc) {
                        var fd = new FormData();
                        fd.append('__RequestVerificationToken', pageToken());
                        appendPayload(fd, rc.payload);
                        return fetch('/account/e2ee-setup', { method: 'POST', body: fd, headers: { 'X-Requested-With': 'fetch' } })
                            .then(function (resp) {
                                if (!resp.ok) {
                                    return resp.json().then(
                                        function (j) { throw new Error(j && j.error ? j.error : 'setup-failed'); },
                                        function () { throw new Error('setup-failed'); });
                                }
                            })
                            .then(function () { return showRecovery(host, rc.recoveryCode); })
                            .then(function () { selfP = E2EE.loadSelf(); return selfP; });
                    });
                });
            });
    }

    // Inline password prompt rendered into `host`. Resolves with the entered password,
    // re-prompting on a wrong password (unlock) until success or cancel (reject).
    function promptPassword(host, hasKeys) {
        return new Promise(function (resolve, reject) {
            host.textContent = '';
            host.setAttribute('style', 'display:block;background:#241708;border:1px solid #f26822;border-radius:8px;padding:14px;margin-bottom:12px;');
            var h = el('div', hasKeys ? 'Unlock your encryption keys' : 'Finish encryption setup to send messages',
                'font-weight:700;margin-bottom:6px;color:#fff;');
            var p = el('div', hasKeys
                ? 'This device hasn’t unlocked your encrypted messages yet. Enter your account password to unlock them here.'
                : 'Your account was created before encrypted messaging, so it has no keys yet. Enter your account password to generate them. This happens in your browser — the server never sees your password or private keys.',
                'color:#d9c7b3;font-size:.9rem;margin-bottom:10px;line-height:1.45;');
            var err = el('div', '', 'color:#ff9a9a;font-size:.85rem;margin-bottom:8px;display:none;');
            var pw = document.createElement('input');
            pw.type = 'password'; pw.placeholder = 'Your account password'; pw.autocomplete = 'current-password';
            pw.setAttribute('style', 'width:100%;box-sizing:border-box;margin-bottom:10px;');
            var btn = document.createElement('button');
            btn.type = 'button'; btn.className = 'btn btn-primary';
            btn.textContent = hasKeys ? 'Unlock' : 'Enable encryption';
            host.appendChild(h); host.appendChild(p); host.appendChild(err); host.appendChild(pw); host.appendChild(btn);
            pw.focus();
            pw.addEventListener('keydown', function (e) { if (e.key === 'Enter') { btn.click(); } });
            btn.addEventListener('click', function () {
                var pass = pw.value;
                if (!pass) { err.textContent = 'Enter your password.'; err.style.display = 'block'; return; }
                btn.disabled = true; btn.textContent = hasKeys ? 'Unlocking…' : 'Setting up…'; err.style.display = 'none';
                resolve(pass);
            });
        });
    }

    // One-time recovery-code display; resolves after the user confirms they saved it.
    function showRecovery(host, code) {
        return new Promise(function (resolve) {
            host.textContent = '';
            host.setAttribute('style', 'display:block;background:#161616;border:1px solid #555;border-radius:8px;padding:16px;margin-bottom:12px;');
            host.appendChild(el('div', 'Save your recovery code', 'font-weight:700;margin-bottom:6px;color:#fff;'));
            host.appendChild(el('div', 'This is the only way to recover your encrypted messages if you forget your password. We cannot recover it for you. Write it down and keep it safe.',
                'color:#bbb;font-size:.88rem;margin-bottom:12px;line-height:1.5;'));
            host.appendChild(el('div', code, 'font-family:monospace;font-size:1.05rem;letter-spacing:1px;background:#0c0c0c;border:1px solid #555;border-radius:8px;padding:12px;word-break:break-all;text-align:center;color:#f0b34d;margin-bottom:12px;'));
            var lbl = document.createElement('label');
            lbl.setAttribute('style', 'display:flex;gap:8px;align-items:center;margin-bottom:12px;cursor:pointer;color:#ddd;');
            var cb = document.createElement('input'); cb.type = 'checkbox';
            lbl.appendChild(cb); lbl.appendChild(document.createTextNode('I have written down my recovery code'));
            var go = document.createElement('button'); go.type = 'button'; go.className = 'btn btn-primary';
            go.textContent = 'Continue'; go.disabled = true; go.style.opacity = '.5';
            cb.addEventListener('change', function () { go.disabled = !cb.checked; go.style.opacity = cb.checked ? '1' : '.5'; });
            go.addEventListener('click', function () { resolve(); });
            host.appendChild(lbl); host.appendChild(go);
        });
    }

    function el(tag, text, style) {
        var e = document.createElement(tag);
        if (text) { e.textContent = text; }
        if (style) { e.setAttribute('style', style); }
        return e;
    }

    // Warn (never block) when a RECEIVED message's decrypted text contains a link. Mirrors the
    // server-side check for plaintext messages (LinkHeuristics.HasLink).
    var LINK_RE = /(https?:\/\/|www\.|\b[a-z0-9-]+\.(?:com|net|org|io|co|xyz|onion|to|cc|me|info|app|dev|gg|link|top|site|xmr|ru|eu|de|uk))([/?#]|\b)/i;
    function flagLinkWarning(node, text, incoming) {
        if (!incoming || !text) { return; }
        var bubble = node.closest ? node.closest('.dm-bubble') : null;
        if (!bubble || bubble.querySelector('.dm-link-warn')) { return; }
        if (!LINK_RE.test(text)) { return; }
        bubble.appendChild(el('div', '⚠ This message contains a link — double-check it before opening.', null)).className = 'dm-link-warn';
    }

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
                    var incoming = String(env.from) !== String(c.me.userId);
                    var signPub = incoming ? c.partner.signPub : c.mySignPub;
                    E2EE.decryptMessage(envJson, { myId: c.me.userId, encPriv: c.me.encPriv, senderSignPub: signPub })
                        .then(function (pt) { node.textContent = pt; flagLinkWarning(node, pt, incoming); })
                        .catch(function () { node.textContent = '🔒 (unable to decrypt)'; });
                });
            }).catch(function (e) {
                nodes.forEach(function (node) {
                    node.textContent = (e && e.message === 'locked') ? '🔒 Unlock your keys to read' : '🔒 (unavailable)';
                });
                if (e && e.message === 'locked') {
                    showBanner('Your encryption keys aren’t set up on this device, so messages can’t be read or sent here.', true);
                }
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
                sendReply(text);
            });

            function sendReply(text) {
                clearBanner();
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
                }).then(function () { if (ta) { ta.value = ''; } clearBanner(); poll(); })
                    .catch(function (e) {
                        if (e && e.message === 'locked') {
                            repairKeys().then(function () { ctx = null; sendReply(text); })
                                .catch(function () { showBanner('Encryption setup was cancelled — your message wasn’t sent.', true); });
                            return;
                        }
                        showBanner(e && e.message === 'no-keys'
                            ? 'That member hasn’t set up encryption yet, so you can’t message them.'
                            : 'Could not send the message. Please try again.', true);
                    });
            }
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
            clearBanner();
            attemptSend(to, text);
        });

        function attemptSend(to, text) {
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
                if (e && e.message === 'locked') {
                    // Sender has no usable keys — repair in place, then resend automatically.
                    repairKeys().then(function () { attemptSend(to, text); })
                        .catch(function (err) {
                            showBanner(err && err.message === 'no-ui'
                                ? 'Could not start encryption setup. Reload the page and try again.'
                                : 'Encryption setup was cancelled — your message wasn’t sent.', true);
                        });
                    return;
                }
                showBanner(e && e.message === 'no-keys'
                    ? 'That member hasn’t set up encryption yet, so you can’t message them.'
                    : 'Could not send the message. Please try again.', true);
            });
        }
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
