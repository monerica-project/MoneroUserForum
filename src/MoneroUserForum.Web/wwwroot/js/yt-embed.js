// Click-to-play for YouTube facades rendered by ForumMarkup.
// Nothing is requested from YouTube until the user clicks Play, so the page
// always loads (important on Tor/VPN), and there is a visible fallback link.
(function () {
  function play(box) {
    var id = box.getAttribute('data-ytid');
    if (!id || !/^[A-Za-z0-9_-]{11}$/.test(id)) { return; }
    var iframe = document.createElement('iframe');
    iframe.className = 'yt-frame';
    iframe.setAttribute('src', 'https://www.youtube-nocookie.com/embed/' + id + '?autoplay=1&rel=0');
    iframe.setAttribute('title', 'YouTube video player');
    iframe.setAttribute('frameborder', '0');
    iframe.setAttribute('allow', 'accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share');
    iframe.setAttribute('allowfullscreen', '');
    iframe.setAttribute('referrerpolicy', 'strict-origin-when-cross-origin');

    var fallback = box.querySelector('.yt-fallback');
    box.innerHTML = '';
    box.appendChild(iframe);
    if (fallback) { box.appendChild(fallback); }  // keep the "open on YouTube" link visible
    box.classList.add('yt-loaded');
  }

  // Delegated so it works for posts loaded now or later.
  document.addEventListener('click', function (e) {
    var btn = e.target.closest ? e.target.closest('.yt-play') : null;
    if (!btn) { return; }
    var box = btn.closest('.yt-embed');
    if (box) { e.preventDefault(); play(box); }
  });

  // If the real thumbnail can't load (Tor/VPN blocks Google), hide the broken
  // image and fall back to the dark card. Capture phase catches img error events
  // (which don't bubble), including previews inserted after load.
  function hideBrokenThumb(img) {
    if (!img || !img.classList || !img.classList.contains('yt-thumb')) { return; }
    img.style.display = 'none';
    var box = img.closest('.yt-embed');
    if (box) { box.classList.add('yt-nothumb'); }
  }
  document.addEventListener('error', function (e) { hideBrokenThumb(e.target); }, true);
  document.addEventListener('DOMContentLoaded', function () {
    var t = document.querySelectorAll('.yt-thumb');
    for (var i = 0; i < t.length; i++) {
      if (t[i].complete && t[i].naturalWidth === 0) { hideBrokenThumb(t[i]); }
    }
  });
})();
