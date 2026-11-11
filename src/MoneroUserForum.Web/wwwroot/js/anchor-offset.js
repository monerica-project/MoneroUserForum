// The header is sticky and its height changes (it wraps on phones). Publish its real height as a CSS
// variable so "#post-123" links land with the post just below the header, not hidden behind it.
(function () {
  function measure() {
    var h = document.querySelector('.site-header');
    if (!h) { return; }
    document.documentElement.style.setProperty('--header-h', Math.ceil(h.getBoundingClientRect().height) + 'px');
  }
  function settle() {
    measure();
    // The browser already jumped before the height was known: jump again, now with the right offset.
    if (location.hash.length > 1) {
      var el = document.getElementById(decodeURIComponent(location.hash.slice(1)));
      if (el) { el.scrollIntoView(); }
    }
  }
  if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', settle); } else { settle(); }
  window.addEventListener('load', settle);
  window.addEventListener('resize', measure);
})();
