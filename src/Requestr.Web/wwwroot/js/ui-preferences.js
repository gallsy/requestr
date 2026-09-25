// Loaded synchronously in <head> so theme and sidebar state apply before first paint.
// Cookies mirror the resolved state so the server can prerender matching markup (see UiPreferences.cs).
(function () {
    var root = document.documentElement;
    var themeKey = 'theme';
    var sidebarKey = 'requestr.sidebarCollapsed';
    var darkQuery = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
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

    // 'light' | 'dark' | 'system'; anything else (including nothing stored) follows the OS.
    function preference() {
        var stored = read(themeKey);
        return stored === 'dark' || stored === 'light' ? stored : 'system';
    }

    function applyTheme() {
        var pref = preference();
        var theme = pref === 'system' ? (darkQuery && darkQuery.matches ? 'dark' : 'light') : pref;
        root.setAttribute('data-theme', theme);
        root.setAttribute('data-bs-theme', theme);
        setCookie('requestr_theme', theme);
        return theme;
    }

    function notify() {
        if (themeListener) {
            themeListener.invokeMethodAsync('SyncThemePreference', preference()).catch(function () { });
        }
    }

    function applySidebar(collapsed) {
        if (collapsed) {
            root.setAttribute('data-sidebar', 'collapsed');
        } else {
            root.removeAttribute('data-sidebar');
        }
        setCookie('requestr_sidebar', collapsed ? 'collapsed' : 'expanded');
    }

    applyTheme();
    applySidebar(read(sidebarKey) === 'true');

    if (darkQuery && darkQuery.addEventListener) {
        darkQuery.addEventListener('change', function () {
            if (preference() === 'system') applyTheme();
        });
    }

    // Keep other open tabs in sync when the theme is changed.
    window.addEventListener('storage', function (e) {
        if (e.key !== themeKey) return;
        applyTheme();
        notify();
    });

    window.requestrUi = {
        isDarkTheme: function () { return root.getAttribute('data-theme') === 'dark'; },
        getThemePreference: preference,
        // Returns true when the resulting theme is dark.
        setThemePreference: function (pref) {
            write(themeKey, pref === 'dark' || pref === 'light' ? pref : 'system');
            return applyTheme() === 'dark';
        },
        subscribeTheme: function (dotNetRef) { themeListener = dotNetRef; },
        isSidebarCollapsed: function () { return root.getAttribute('data-sidebar') === 'collapsed'; },
        setSidebarCollapsed: function (collapsed) {
            write(sidebarKey, collapsed ? 'true' : 'false');
            applySidebar(!!collapsed);
        }
    };
})();
