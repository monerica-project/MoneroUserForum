/*
 * MoneroUserForum end-to-end encryption core (WebCrypto, no dependencies).
 *
 * Design (see the /messages/encryption threat-model page):
 *  - Each member has two long-term keypairs generated IN THE BROWSER: ECDH P-256
 *    (encryption) and ECDSA P-256 (signatures). Public halves go to the server's
 *    directory; private halves are AES-GCM-wrapped under a key derived from the
 *    member's password (PBKDF2) and stored server-side only in that opaque form.
 *  - The server can never unwrap them, so a DB dump cannot read any message.
 *  - A message is encrypted once with a random AES-256-GCM key; that key is then
 *    wrapped (via ephemeral ECDH) to BOTH the recipient and the sender, so each can
 *    read it. The sender signs the envelope so the recipient knows it is authentic.
 *  - Private keys live in IndexedDB as NON-EXTRACTABLE CryptoKey objects.
 *
 * Everything here runs client-side. window.E2EE is the public surface.
 */
(function () {
    'use strict';

    var subtle = (window.crypto && window.crypto.subtle) || null;
    var KDF_ITERATIONS = 600000;
    var IDB_NAME = 'muf-e2ee';
    var IDB_STORE = 'self';
    var IDB_PEERS = 'peers';

    // ---- small helpers ---------------------------------------------------
    function b64(buf) {
        var bytes = new Uint8Array(buf), s = '';
        for (var i = 0; i < bytes.length; i++) { s += String.fromCharCode(bytes[i]); }
        return btoa(s);
    }
    function unb64(str) {
        var s = atob(str), bytes = new Uint8Array(s.length);
        for (var i = 0; i < s.length; i++) { bytes[i] = s.charCodeAt(i); }
        return bytes.buffer;
    }
    function utf8(str) { return new TextEncoder().encode(str); }
    function fromUtf8(buf) { return new TextDecoder().decode(buf); }
    function rand(n) { return window.crypto.getRandomValues(new Uint8Array(n)); }

    function supported() {
        return !!(subtle && window.crypto.getRandomValues && window.indexedDB);
    }

    // ---- key import / export --------------------------------------------
    function exportSpki(key) { return subtle.exportKey('spki', key).then(b64); }

    function importEncPub(spkiB64) {
        return subtle.importKey('spki', unb64(spkiB64), { name: 'ECDH', namedCurve: 'P-256' }, true, []);
    }
    function importSignPub(spkiB64) {
        return subtle.importKey('spki', unb64(spkiB64), { name: 'ECDSA', namedCurve: 'P-256' }, true, ['verify']);
    }

    function generateIdentity() {
        // Generated extractable so the private halves can be wrapped; the copies we
        // persist to IndexedDB are re-imported as non-extractable (see storeSelf).
        return Promise.all([
            subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']),
            subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify'])
        ]).then(function (pairs) {
            return { enc: pairs[0], sign: pairs[1] };
        });
    }

    // ---- password / recovery key derivation -----------------------------
    function deriveWrapKey(secretBytes, saltB64, iterations) {
        return subtle.importKey('raw', secretBytes, 'PBKDF2', false, ['deriveKey']).then(function (base) {
            return subtle.deriveKey(
                { name: 'PBKDF2', salt: new Uint8Array(unb64(saltB64)), iterations: iterations, hash: 'SHA-256' },
                base, { name: 'AES-GCM', length: 256 }, false, ['wrapKey', 'unwrapKey', 'encrypt', 'decrypt']);
        });
    }
    function deriveKekFromPassword(password, saltB64) {
        return deriveWrapKey(utf8(password), saltB64, KDF_ITERATIONS);
    }
    function deriveKeyFromRecovery(codeBytes, saltB64) {
        // The recovery code is already 256 bits of entropy, so a light KDF is enough.
        return deriveWrapKey(codeBytes, saltB64, 2048);
    }

    // ---- wrap / unwrap the private key bundle ---------------------------
    function wrapPrivateBundle(wrapKey, identity) {
        return Promise.all([
            subtle.exportKey('pkcs8', identity.enc.privateKey),
            subtle.exportKey('pkcs8', identity.sign.privateKey)
        ]).then(function (pk) {
            var bundle = JSON.stringify({ enc: b64(pk[0]), sign: b64(pk[1]) });
            var iv = rand(12);
            return subtle.encrypt({ name: 'AES-GCM', iv: iv }, wrapKey, utf8(bundle)).then(function (ct) {
                return JSON.stringify({ iv: b64(iv), ct: b64(ct) });
            });
        });
    }

    // Returns { encPriv, signPriv } imported NON-EXTRACTABLE.
    function unwrapPrivateBundle(wrapKey, wrappedJson) {
        var w = JSON.parse(wrappedJson);
        return subtle.decrypt({ name: 'AES-GCM', iv: new Uint8Array(unb64(w.iv)) }, wrapKey, unb64(w.ct))
            .then(function (plain) {
                var bundle = JSON.parse(fromUtf8(plain));
                return Promise.all([
                    subtle.importKey('pkcs8', unb64(bundle.enc), { name: 'ECDH', namedCurve: 'P-256' }, false, ['deriveBits']),
                    subtle.importKey('pkcs8', unb64(bundle.sign), { name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign'])
                ]).then(function (keys) { return { encPriv: keys[0], signPriv: keys[1] }; });
            });
    }

    // ---- recovery code ---------------------------------------------------
    var B32 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
    function newRecoveryCode() {
        var bytes = rand(32), out = '';
        for (var i = 0; i < bytes.length; i++) { out += B32[bytes[i] % 32]; }
        // group into blocks of 5 for readability: XXXXX-XXXXX-...
        var groups = out.match(/.{1,5}/g);
        return { display: groups.join('-'), bytes: bytes };
    }
    function recoveryBytesFrom(display) {
        var clean = (display || '').toUpperCase().replace(/[^A-Z2-7]/g, '');
        var bytes = new Uint8Array(clean.length);
        for (var i = 0; i < clean.length; i++) { bytes[i] = B32.indexOf(clean[i]); }
        return bytes;
    }

    // ---- IndexedDB (device-local, non-extractable keys) -----------------
    function idb() {
        return new Promise(function (resolve, reject) {
            var req = window.indexedDB.open(IDB_NAME, 1);
            req.onupgradeneeded = function () {
                var db = req.result;
                if (!db.objectStoreNames.contains(IDB_STORE)) { db.createObjectStore(IDB_STORE); }
                if (!db.objectStoreNames.contains(IDB_PEERS)) { db.createObjectStore(IDB_PEERS); }
            };
            req.onsuccess = function () { resolve(req.result); };
            req.onerror = function () { reject(req.error); };
        });
    }
    function idbSet(store, key, val) {
        return idb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction(store, 'readwrite');
                tx.objectStore(store).put(val, key);
                tx.oncomplete = function () { resolve(); };
                tx.onerror = function () { reject(tx.error); };
            });
        });
    }
    function idbGet(store, key) {
        return idb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction(store, 'readonly');
                var r = tx.objectStore(store).get(key);
                r.onsuccess = function () { resolve(r.result || null); };
                r.onerror = function () { reject(r.error); };
            });
        });
    }

    function storeSelf(userId, encPriv, signPriv, encPubB64, signPubB64) {
        return idbSet(IDB_STORE, 'me', {
            userId: userId, encPriv: encPriv, signPriv: signPriv,
            encPub: encPubB64, signPub: signPubB64
        });
    }
    function loadSelf() { return idbGet(IDB_STORE, 'me'); }
    function clearSelf() { return idbSet(IDB_STORE, 'me', null); }

    // Register generates keys before the server has assigned a user id; once known
    // (from a page meta tag) we stamp it onto the cached record.
    function tagUserId(userId) {
        return loadSelf().then(function (me) {
            if (me && me.userId !== userId) { me.userId = userId; return idbSet(IDB_STORE, 'me', me); }
        });
    }

    // Trust-on-first-use peer public keys, so we can warn if a key changes.
    function rememberPeer(userId, encPubB64, signPubB64) {
        return idbGet(IDB_PEERS, String(userId)).then(function (prev) {
            var changed = prev && (prev.encPub !== encPubB64 || prev.signPub !== signPubB64);
            return idbSet(IDB_PEERS, String(userId), { encPub: encPubB64, signPub: signPubB64 })
                .then(function () { return { firstSeen: !prev, changed: !!changed }; });
        });
    }

    // ---- message envelope: encrypt / decrypt ----------------------------
    function ecdhWrapKey(ephPriv, peerEncPub) {
        return subtle.deriveBits({ name: 'ECDH', public: peerEncPub }, ephPriv, 256).then(function (shared) {
            return subtle.importKey('raw', shared, 'HKDF', false, ['deriveKey']).then(function (hk) {
                return subtle.deriveKey(
                    { name: 'HKDF', hash: 'SHA-256', salt: new Uint8Array(32), info: utf8('muf-wrap-msgkey-v1') },
                    hk, { name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
            });
        });
    }

    function sigInput(env) {
        var ids = Object.keys(env.keys).sort(function (a, b) { return a - b; });
        var parts = [env.v, env.from, env.to, env.ts, env.iv, env.ct];
        for (var i = 0; i < ids.length; i++) {
            var k = env.keys[ids[i]];
            parts.push(ids[i] + ':' + k.eph + ':' + k.iv + ':' + k.wk);
        }
        return utf8(parts.join('|'));
    }

    // parties: [{ id, encPub(CryptoKey) }, ...] — include BOTH recipient and sender.
    function encryptMessage(plaintext, opts) {
        var ts = opts.ts || Date.now();
        var aad = utf8(opts.from + ':' + opts.to + ':' + ts);
        var iv = rand(12);
        return subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt']).then(function (msgKey) {
            return subtle.encrypt({ name: 'AES-GCM', iv: iv, additionalData: aad }, msgKey, utf8(plaintext)).then(function (ct) {
                return subtle.exportKey('raw', msgKey).then(function (rawMsgKey) {
                    var keys = {};
                    var chain = Promise.resolve();
                    opts.parties.forEach(function (p) {
                        chain = chain.then(function () {
                            return subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']).then(function (eph) {
                                return ecdhWrapKey(eph.privateKey, p.encPub).then(function (wrapKey) {
                                    var wkIv = rand(12);
                                    return subtle.encrypt({ name: 'AES-GCM', iv: wkIv }, wrapKey, rawMsgKey).then(function (wk) {
                                        return exportSpki(eph.publicKey).then(function (ephB64) {
                                            keys[p.id] = { eph: ephB64, iv: b64(wkIv), wk: b64(wk) };
                                        });
                                    });
                                });
                            });
                        });
                    });
                    return chain.then(function () {
                        var env = { v: 1, from: opts.from, to: opts.to, ts: ts, iv: b64(iv), ct: b64(ct), keys: keys };
                        return subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, opts.signPriv, sigInput(env)).then(function (sig) {
                            env.sig = b64(sig);
                            return JSON.stringify(env);
                        });
                    });
                });
            });
        });
    }

    // opts: { myId, encPriv, senderSignPub(CryptoKey) }
    function decryptMessage(envelopeJson, opts) {
        var env = JSON.parse(envelopeJson);
        return subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, opts.senderSignPub, unb64(env.sig), sigInput(env))
            .then(function (ok) {
                if (!ok) { throw new Error('bad-signature'); }
                var k = env.keys[String(opts.myId)];
                if (!k) { throw new Error('not-a-recipient'); }
                return importEncPub(k.eph).then(function (ephPub) {
                    return subtle.deriveBits({ name: 'ECDH', public: ephPub }, opts.encPriv, 256).then(function (shared) {
                        return subtle.importKey('raw', shared, 'HKDF', false, ['deriveKey']).then(function (hk) {
                            return subtle.deriveKey(
                                { name: 'HKDF', hash: 'SHA-256', salt: new Uint8Array(32), info: utf8('muf-wrap-msgkey-v1') },
                                hk, { name: 'AES-GCM', length: 256 }, false, ['decrypt']).then(function (wrapKey) {
                                return subtle.decrypt({ name: 'AES-GCM', iv: new Uint8Array(unb64(k.iv)) }, wrapKey, unb64(k.wk)).then(function (rawMsgKey) {
                                    return subtle.importKey('raw', rawMsgKey, { name: 'AES-GCM' }, false, ['decrypt']).then(function (msgKey) {
                                        var aad = utf8(env.from + ':' + env.to + ':' + env.ts);
                                        return subtle.decrypt({ name: 'AES-GCM', iv: new Uint8Array(unb64(env.iv)), additionalData: aad }, msgKey, unb64(env.ct))
                                            .then(function (pt) { return fromUtf8(pt); });
                                    });
                                });
                            });
                        });
                    });
                });
            });
    }

    // ---- safety number (fingerprint of the two public keys) -------------
    function fingerprint(encPubB64, signPubB64) {
        return subtle.digest('SHA-256', utf8(encPubB64 + '|' + signPubB64)).then(function (h) {
            var bytes = new Uint8Array(h), out = [];
            for (var i = 0; i < 15; i += 5) {
                var n = (bytes[i] << 24 | bytes[i + 1] << 16 | bytes[i + 2] << 8 | bytes[i + 3]) >>> 0;
                out.push(('00000' + (n % 100000)).slice(-5));
            }
            return out.join(' ');
        });
    }

    // ---- high-level: set up keys / unlock -------------------------------
    // Generates identity, wraps under password + a fresh recovery code, and returns
    // the payload to POST to the server plus the recovery code to show once.
    function createIdentityPayload(userId, password) {
        var kdfSalt = b64(rand(16));
        var recSalt = b64(rand(16));
        var rec = newRecoveryCode();
        return generateIdentity().then(function (identity) {
            return Promise.all([
                exportSpki(identity.enc.publicKey),
                exportSpki(identity.sign.publicKey),
                deriveKekFromPassword(password, kdfSalt),
                deriveKeyFromRecovery(rec.bytes, recSalt)
            ]).then(function (r) {
                var encPubB64 = r[0], signPubB64 = r[1], kek = r[2], recKey = r[3];
                return Promise.all([
                    wrapPrivateBundle(kek, identity),
                    wrapPrivateBundle(recKey, identity)
                ]).then(function (wrapped) {
                    // Persist non-extractable copies to this device.
                    return Promise.all([
                        subtle.exportKey('pkcs8', identity.enc.privateKey).then(function (p) {
                            return subtle.importKey('pkcs8', p, { name: 'ECDH', namedCurve: 'P-256' }, false, ['deriveBits']);
                        }),
                        subtle.exportKey('pkcs8', identity.sign.privateKey).then(function (p) {
                            return subtle.importKey('pkcs8', p, { name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign']);
                        })
                    ]).then(function (nonExt) {
                        return storeSelf(userId, nonExt[0], nonExt[1], encPubB64, signPubB64).then(function () {
                            return {
                                payload: {
                                    encPublicKey: encPubB64, signPublicKey: signPubB64,
                                    kdfSalt: kdfSalt, wrappedPrivateKeys: wrapped[0],
                                    recoverySalt: recSalt, recoveryWrappedPrivateKeys: wrapped[1]
                                },
                                recoveryCode: rec.display
                            };
                        });
                    });
                });
            });
        });
    }

    // Unlock existing keys with the password and cache them on this device.
    function unlockWithPassword(userId, password, keyRecord) {
        return deriveKekFromPassword(password, keyRecord.kdfSalt).then(function (kek) {
            return unwrapPrivateBundle(kek, keyRecord.wrappedPrivateKeys).then(function (priv) {
                return storeSelf(userId, priv.encPriv, priv.signPriv, keyRecord.encPublicKey, keyRecord.signPublicKey)
                    .then(function () { return true; });
            });
        });
    }

    window.E2EE = {
        supported: supported,
        createIdentityPayload: createIdentityPayload,
        unlockWithPassword: unlockWithPassword,
        loadSelf: loadSelf,
        clearSelf: clearSelf,
        tagUserId: tagUserId,
        rememberPeer: rememberPeer,
        importEncPub: importEncPub,
        importSignPub: importSignPub,
        encryptMessage: encryptMessage,
        decryptMessage: decryptMessage,
        fingerprint: fingerprint,
        deriveKeyFromRecovery: deriveKeyFromRecovery,
        recoveryBytesFrom: recoveryBytesFrom,
        unwrapPrivateBundle: unwrapPrivateBundle
    };
})();
