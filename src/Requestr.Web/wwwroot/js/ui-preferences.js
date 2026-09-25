// Loaded synchronously in <head> so theme and sidebar state apply before first paint.
// Cookies mirror localStorage so the server can prerender matching markup (see UiPreferences.cs).
(function () {
    var root = document.documentElement;
    var themeKey = 'theme';
    var sidebarKey = 'requestr.sidebarCollapsed';
    var themeListener = null;

    function read(key) {
        try { return localStorage.getItem(key); } catch (e) { return null; }
    }

    function write(key, value) {
        try { localStorage.setItem(key, value); } catch (e) { /* storage unavailable */ }
    }

    function setCookie(name, value) {
        document.cookie = name + '=' + value + '; path=/; max-age=31536000; SameSite=Lax' +
            (location.protocol === 'https:' ? '; Secure' : '');
    }

    function applyTheme(theme) {
        root.setAttribute('data-theme', theme);
        root.setAttribute('data-bs-theme', theme);
        setCookie('requestr_theme', theme);
    }

    function applySidebar(collapsed) {
        if (collapsed) {
            root.setAttribute('data-sidebar', 'collapsed');
        } else {
            root.removeAttribute('data-sidebar');
        }
        setCookie('requestr_sidebar', collapsed ? 'collapsed' : 'expanded');
    }

    var storedTheme = read(themeKey);
    var prefersDark = !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
    applyTheme(storedTheme === 'dark' || storedTheme === 'light' ? storedTheme : (prefersDark ? 'dark' : 'light'));
    applySidebar(read(sidebarKey) === 'true');

    // Keep other open tabs in sync when the theme is toggled.
    window.addEventListener('storage', function (e) {
        if (e.key !== themeKey || (e.newValue !== 'dark' && e.newValue !== 'light')) return;
        applyTheme(e.newValue);
        if (themeListener) {
            themeListener.invokeMethodAsync('SyncTheme', e.newValue === 'dark').catch(function () { });
        }
    });

    window.requestrUi = {
        isDarkTheme: function () { return root.getAttribute('data-theme') === 'dark'; },
        setTheme: function (isDark) {
            var theme = isDark ? 'dark' : 'light';
            write(themeKey, theme);
            applyTheme(theme);
        },
        subscribeTheme: function (dotNetRef) { themeListener = dotNetRef; },
        isSidebarCollapsed: function () { return root.getAttribute('data-sidebar') === 'collapsed'; },
        setSidebarCollapsed: function (collapsed) {
            write(sidebarKey, collapsed ? 'true' : 'false');
            applySidebar(!!collapsed);
        }
    };
})();
