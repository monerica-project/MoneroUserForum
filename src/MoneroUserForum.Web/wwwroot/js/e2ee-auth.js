/*
 * Ties the E2EE key lifecycle into signup and login.
 *  - Register: generate keys in-browser, inject them as hidden fields, show the
 *    one-time recovery code, then submit the form normally (captcha untouched).
 *  - Login: submit via fetch so the password stays in memory; on success either
 *    UNLOCK the existing keys or, for an account without keys yet, generate them
 *    and show the recovery code — then navigate.
 * Degrades gracefully: if WebCrypto/IndexedDB is unavailable, forms submit as normal.
 */
(function () {
    'use strict';
    if (!window.E2EE || !E2EE.supported()) { return; }

    function metaContent(name) {
        var m = document.querySelector('meta[name="' + name + '"]');
        return m ? m.getAttribute('content') : null;
    }
    function tokenValue(form) {
        var i = form.querySelector('input[name="__RequestVerificationToken"]');
        return i ? i.value : '';
    }
    function setHidden(form, name, val) {
        var i = form.querySelector('input[name="' + name + '"]');
        if (!i) { i = document.createElement('input'); i.type = 'hidden'; i.name = name; form.appendChild(i); }
        i.value = val;
    }
    function appendPayload(fd, p) {
        fd.append('EncPublicKey', p.encPublicKey);
        fd.append('SignPublicKey', p.signPublicKey);
        fd.append('KdfSalt', p.kdfSalt);
        fd.append('WrappedPrivateKeys', p.wrappedPrivateKeys);
        fd.append('RecoverySalt', p.recoverySalt);
        fd.append('RecoveryWrappedPrivateKeys', p.recoveryWrappedPrivateKeys);
    }

    // ---- one-time recovery-code overlay (inline styles: CSP allows style attrs) ----
    function showRecovery(code) {
        return new Promise(function (resolve) {
            var ov = document.createElement('div');
            ov.setAttribute('style', 'position:fixed;inset:0;background:rgba(0,0,0,.72);display:flex;align-items:center;justify-content:center;z-index:9999;padding:16px;');
            var box = document.createElement('div');
            box.setAttribute('style', 'background:#161616;color:#eee;border:1px solid #444;border-radius:10px;max-width:460px;width:100%;padding:22px;font-family:inherit;');
            var h = document.createElement('h2'); h.textContent = 'Save your recovery code';
            h.setAttribute('style', 'margin:0 0 8px;font-size:1.2rem;');
            var p = document.createElement('p');
            p.textContent = 'This is the only way to recover your encrypted messages if you forget your password. We cannot recover it for you — not now, not ever. Write it down and keep it somewhere safe.';
            p.setAttribute('style', 'margin:0 0 14px;line-height:1.5;color:#bbb;');
            var codeEl = document.createElement('div'); codeEl.textContent = code;
            codeEl.setAttribute('style', 'font-family:monospace;font-size:1.15rem;letter-spacing:1px;background:#0c0c0c;border:1px solid #555;border-radius:8px;padding:14px;word-break:break-all;text-align:center;color:#f0b34d;margin-bottom:14px;');
            var lbl = document.createElement('label');
            lbl.setAttribute('style', 'display:flex;gap:8px;align-items:center;margin-bottom:16px;cursor:pointer;');
            var cb = document.createElement('input'); cb.type = 'checkbox';
            lbl.appendChild(cb); lbl.appendChild(document.createTextNode('I have written down my recovery code'));
            var btn = document.createElement('button'); btn.type = 'button'; btn.textContent = 'Continue'; btn.disabled = true;
            btn.setAttribute('style', 'width:100%;padding:11px;border-radius:8px;border:0;background:#f26822;color:#111;font-weight:600;cursor:pointer;');
            cb.addEventListener('change', function () { btn.disabled = !cb.checked; btn.style.opacity = cb.checked ? '1' : '.5'; });
            btn.style.opacity = '.5';
            btn.addEventListener('click', function () { document.body.removeChild(ov); resolve(); });
            box.appendChild(h); box.appendChild(p); box.appendChild(codeEl); box.appendChild(lbl); box.appendChild(btn);
            ov.appendChild(box); document.body.appendChild(ov);
        });
    }

    // ---- register ----
    var reg = document.getElementById('register-form');
    if (reg) {
        var regSubmitting = false;
        reg.addEventListener('submit', function (ev) {
            if (regSubmitting) { return; } // second pass: let the real submit through
            var pwEl = reg.querySelector('input[name="Password"]');
            var confirmEl = reg.querySelector('input[name="ConfirmPassword"]');
            var pw = pwEl ? pwEl.value : '';
            // If the password is empty or mismatched, let the server render its validation.
            if (!pw || (confirmEl && confirmEl.value !== pw)) { return; }
            ev.preventDefault();
            E2EE.createIdentityPayload(0, pw).then(function (res) {
                var p = res.payload;
                setHidden(reg, 'EncPublicKey', p.encPublicKey);
                setHidden(reg, 'SignPublicKey', p.signPublicKey);
                setHidden(reg, 'KdfSalt', p.kdfSalt);
                setHidden(reg, 'WrappedPrivateKeys', p.wrappedPrivateKeys);
                setHidden(reg, 'RecoverySalt', p.recoverySalt);
                setHidden(reg, 'RecoveryWrappedPrivateKeys', p.recoveryWrappedPrivateKeys);
                return showRecovery(res.recoveryCode);
            }).then(function () {
                regSubmitting = true;
                reg.submit();
            }).catch(function (e) {
                console.error('E2EE key setup failed; submitting without encryption keys.', e);
                regSubmitting = true;
                reg.submit();
            });
        });
    }

    // ---- login ----
    var login = document.getElementById('login-form');
    if (login) {
        login.addEventListener('submit', function (ev) {
            ev.preventDefault();
            var pwEl = login.querySelector('input[name="Password"]');
            var pw = pwEl ? pwEl.value : '';
            var errBox = document.getElementById('e2ee-login-error');
            var go = function (url) { window.location.href = url || '/'; };
            fetch(login.action, { method: 'POST', body: new FormData(login), headers: { 'X-Requested-With': 'fetch' } })
                .then(function (r) { return r.json().catch(function () { return { ok: false, error: 'Login failed.' }; }); })
                .then(function (j) {
                    if (!j.ok) {
                        if (errBox) { errBox.textContent = j.error || 'Invalid username or password.'; errBox.style.display = 'block'; }
                        return;
                    }
                    // Authenticated (cookie is set). From here, ANY crypto failure must still
                    // navigate — the user is logged in; keys can be unlocked on a later try.
                    var crypto;
                    if (j.hasKeys) {
                        crypto = E2EE.unlockWithPassword(j.userId, pw, j.keys);
                    } else {
                        crypto = E2EE.createIdentityPayload(j.userId, pw).then(function (rc) {
                            var fd = new FormData();
                            fd.append('__RequestVerificationToken', tokenValue(login));
                            appendPayload(fd, rc.payload);
                            return fetch('/account/e2ee-setup', { method: 'POST', body: fd, headers: { 'X-Requested-With': 'fetch' } })
                                .then(function () { return showRecovery(rc.recoveryCode); });
                        });
                    }
                    return crypto.then(function () { go(j.redirect); }, function (e) {
                        console.error('E2EE unlock/setup failed; continuing to site.', e);
                        go(j.redirect);
                    });
                })
                .catch(function (e) {
                    console.error(e);
                    if (errBox) { errBox.textContent = 'Login failed. Please try again.'; errBox.style.display = 'block'; }
                });
        });
    }

    // ---- change password: re-wrap the private keys under the new password ----
    // Without this, changing the password orphans the member's encrypted messages.
    var chpw = document.getElementById('change-password-form');
    if (chpw) {
        var chpwSubmitting = false;
        chpw.addEventListener('submit', function (ev) {
            if (chpwSubmitting) { return; }
            var cur = chpw.querySelector('input[name="CurrentPassword"]');
            var nw = chpw.querySelector('input[name="NewPassword"]');
            var conf = chpw.querySelector('input[name="ConfirmPassword"]');
            if (!cur || !nw || !cur.value || !nw.value || (conf && conf.value !== nw.value)) { return; } // let server validate
            ev.preventDefault();
            fetch('/account/e2ee-keys', { headers: { 'X-Requested-With': 'fetch' } })
                .then(function (r) { return r.json(); })
                .then(function (keys) {
                    if (!keys.hasKeys) { chpwSubmitting = true; chpw.submit(); return; } // no E2EE → plain change
                    return E2EE.rewrapForNewPassword(keys.userId, cur.value, nw.value, keys).then(function (res) {
                        setHidden(chpw, 'NewKdfSalt', res.newKdfSalt);
                        setHidden(chpw, 'NewWrappedPrivateKeys', res.newWrappedPrivateKeys);
                        chpwSubmitting = true;
                        chpw.submit();
                    });
                })
                .catch(function (e) {
                    // Fail SAFE: do not submit — changing the password without re-wrapping would
                    // permanently lock the member out of their existing messages.
                    console.error('E2EE re-wrap failed', e);
                    window.alert('Could not update your encryption keys for the new password. Double-check your current password and try again.');
                });
        });
    }

    // ---- recover encrypted messages with the one-time recovery code ----
    var recoverForm = document.getElementById('recover-keys-form');
    if (recoverForm) {
        recoverForm.addEventListener('submit', function (ev) {
            ev.preventDefault();
            var codeEl = recoverForm.querySelector('input[name="RecoveryCode"]');
            var pwEl = recoverForm.querySelector('input[name="Password"]');
            var errBox = document.getElementById('recover-error');
            var okBox = document.getElementById('recover-ok');
            var code = codeEl ? codeEl.value.trim() : '';
            var pw = pwEl ? pwEl.value : '';
            if (!code || !pw) { return; }
            fetch('/account/e2ee-keys', { headers: { 'X-Requested-With': 'fetch' } })
                .then(function (r) { return r.json(); })
                .then(function (keys) {
                    if (!keys.recoveryWrappedPrivateKeys) { throw new Error('no-recovery'); }
                    return E2EE.unlockWithRecoveryCode(keys.userId, code, pw, keys).then(function (res) {
                        var fd = new FormData();
                        fd.append('__RequestVerificationToken', tokenValue(recoverForm));
                        fd.append('kdfSalt', res.newKdfSalt);
                        fd.append('wrappedPrivateKeys', res.newWrappedPrivateKeys);
                        return fetch('/account/e2ee-update-wrap', { method: 'POST', body: fd, headers: { 'X-Requested-With': 'fetch' } })
                            .then(function (r) { if (!r.ok) { throw new Error('save-failed'); } });
                    });
                })
                .then(function () {
                    if (okBox) { okBox.style.display = 'block'; }
                    window.location.href = '/messages';
                })
                .catch(function (e) {
                    console.error(e);
                    if (errBox) {
                        errBox.textContent = (e && e.message === 'no-recovery')
                            ? 'No recovery data is stored for this account.'
                            : 'Could not recover — check that your recovery code and current password are correct.';
                        errBox.style.display = 'block';
                    }
                });
        });
    }

    // ---- bootstrap: stamp the real user id onto keys generated at signup ----
    var uid = metaContent('muf-user-id');
    if (uid) { E2EE.tagUserId(parseInt(uid, 10)).catch(function () {}); }
})();
