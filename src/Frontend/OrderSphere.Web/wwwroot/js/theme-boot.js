// Runs synchronously in <head>, before the stylesheets and long before Blazor
// starts. Sets the theme and language on <html> so the first paint is already
// correct (no flash) and the boot screen matches the user's theme.
// A classic script, not a module: the production CSP has no 'unsafe-inline'
// for scripts, and a module would be deferred past first paint.
(function () {
  var root = document.documentElement;

  function read(key) {
    try {
      return window.localStorage.getItem(key);
    } catch (e) {
      return null; // private mode / storage disabled
    }
  }

  var stored = read('os-dark-mode');
  var isDark = stored === null
    ? window.matchMedia('(prefers-color-scheme: dark)').matches
    : stored === 'true';

  root.dataset.theme = isDark ? 'dark' : 'light';

  var culture = read('os-culture');
  root.lang = culture ? culture.split('-')[0] : 'de';

  var meta = document.querySelector('meta[name="theme-color"]');
  if (meta) meta.setAttribute('content', isDark ? '#0F0E0C' : '#F7F5F0');

  // Read back by Program.cs so MudThemeProvider renders the right palette on
  // its very first render instead of correcting itself afterwards.
  window.osBoot = {
    isDark: function () {
      return document.documentElement.dataset.theme === 'dark';
    }
  };
})();
