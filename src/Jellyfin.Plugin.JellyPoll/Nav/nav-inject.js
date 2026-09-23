/**
 * JellyPoll nav injection — served at /JellyPoll/nav-inject?v=<cachebust>.
 * Injected into Jellyfin's /web/index.html by NavInjectionMiddleware (server-side
 * transform; the webroot on disk is never touched).
 *
 * Adds a "Jelly Polls" entry to the web UI navigation for ALL users:
 *  - Jellyfin 12 (default layout): the avatar menu (#app-user-menu), below Profile.
 *  - Legacy drawer layouts: the sidebar (.navMenuOption), next to Settings.
 * Re-injects idempotently as the SPA re-renders (MutationObserver).
 */
(function () {
    'use strict';
    if (window.__jellypoll_nav) return;
    window.__jellypoll_nav = true;

    var ENTRY_IDS = ['jellypoll-avatar-item', 'jellypoll-drawer-item'];

    function jellyfinBasePath() {
        try {
            var path = window.location.pathname || '';
            var at = path.toLowerCase().indexOf('/web');
            return at >= 0 ? path.substring(0, at).replace(/\/+$/, '') : '';
        } catch (e) {
            return '';
        }
    }

    function spaUrl() {
        try {
            if (window.ApiClient && typeof window.ApiClient.getUrl === 'function') {
                return window.ApiClient.getUrl('JellyPoll/Web/');
            }
        } catch (e) { /* pathname fallback */ }
        return jellyfinBasePath() + '/JellyPoll/Web/';
    }

    function iconSpan(cls) {
        var s = document.createElement('span');
        s.className = cls || 'navMenuOptionIcon material-icons';
        s.style.fontFamily = 'Material Icons';
        s.style.fontSize = '24px';
        s.style.lineHeight = '1';
        s.setAttribute('aria-hidden', 'true');
        s.textContent = 'how_to_vote';
        return s;
    }

    function labelSpan(cls) {
        var s = document.createElement('span');
        s.className = cls || 'navMenuOptionText';
        s.textContent = 'Jelly Polls';
        return s;
    }

    function goToSpa(e) {
        if (e) e.preventDefault();
        window.location.assign(spaUrl());
    }

    // ---- Jellyfin 12: avatar menu (mounted while closed, like 2FA's entry) ----
    function injectAvatarMenu() {
        try {
            var menu = document.getElementById('app-user-menu');
            if (!menu) return;
            var list = menu.querySelector('ul[role="menu"]') || menu.querySelector('ul.MuiList-root');
            if (!list) return;
            var existing = document.getElementById('jellypoll-avatar-item');
            if (existing && existing.parentNode === list) return;
            if (existing && existing.parentNode) existing.parentNode.removeChild(existing);

            var profile = list.querySelector('a[href*="/userprofile"]')
                || list.querySelector('a.MuiMenuItem-root')
                || list.querySelector('li.MuiMenuItem-root');
            if (!profile) return;

            var item = profile.cloneNode(true);
            item.id = 'jellypoll-avatar-item';
            item.removeAttribute('aria-current');
            item.className = (item.className || '').replace(/\bMui-selected\b/g, '').replace(/\s{2,}/g, ' ').trim();
            var link = item.tagName === 'A' ? item : item.querySelector('a');
            if (link) {
                link.setAttribute('href', spaUrl());
                link.removeAttribute('aria-current');
                link.addEventListener('click', goToSpa);
            }
            var icon = item.querySelector('.MuiListItemIcon-root');
            if (icon) {
                icon.innerHTML = '';
                icon.appendChild(iconSpan('material-icons'));
            }
            var label = item.querySelector('.MuiListItemText-primary')
                || item.querySelector('.MuiListItemText-root .MuiTypography-root')
                || item.querySelector('.MuiListItemText-root');
            if (label) {
                label.textContent = 'Jelly Polls';
            } else if (!link) {
                item.textContent = '';
                item.appendChild(labelSpan(''));
            }
            if (profile && profile.nextSibling) list.insertBefore(item, profile.nextSibling);
            else list.appendChild(item);
        } catch (e) {
            console.error('[JellyPoll] avatar menu inject error:', e);
        }
    }

    // ---- Legacy drawer (hidden on Jellyfin 12's default layout; helps
    //      non-default/older layouts and the official Android web shell) ----
    // Anchor lookup is language-independent (audit fix): href first (JF 10/11
    // drawers use real hrefs like #/mypreferencesmenu), then the stable
    // btnSettings class (JF 12's drawer Settings item has href="#"). Never
    // match on localized label text.
    function findDrawerAnchor(items) {
        for (var i = 0; i < items.length; i++) {
            var href = (items[i].getAttribute('href') || '').toLowerCase();
            if (href.indexOf('mypreferencesmenu') >= 0 || href.indexOf('myprofile') >= 0 || href.indexOf('/userprofile') >= 0) {
                return items[i];
            }
        }
        for (var j = 0; j < items.length; j++) {
            var cls = (items[j].className || '').toString().toLowerCase();
            if (cls.indexOf('btnsettings') >= 0) return items[j];
        }
        return null;
    }

    function injectDrawer() {
        try {
            var items = document.querySelectorAll('.mainDrawer .navMenuOption, .navMenuOption');
            if (!items.length) return;
            var anchor = findDrawerAnchor(items);
            if (!anchor) return;
            var parent = anchor.parentElement;
            if (!parent) return;
            var existing = document.getElementById('jellypoll-drawer-item');
            if (existing && existing.parentNode === parent) return;
            if (existing && existing.parentNode) existing.parentNode.removeChild(existing);

            var a = document.createElement('a');
            a.id = 'jellypoll-drawer-item';
            a.href = spaUrl();
            a.className = anchor.className || 'navMenuOption emby-button';
            a.setAttribute('role', 'menuitem');
            a.style.cursor = 'pointer';
            a.addEventListener('click', goToSpa);
            a.appendChild(iconSpan('material-icons navMenuOptionIcon'));
            a.appendChild(labelSpan('navMenuOptionText'));
            if (anchor.nextSibling) parent.insertBefore(a, anchor.nextSibling);
            else parent.appendChild(a);
        } catch (e) {
            console.error('[JellyPoll] drawer inject error:', e);
        }
    }

    function injectAll() {
        injectAvatarMenu();
        injectDrawer();
    }

    // ---- Re-inject after SPA re-renders ----
    var scheduled = null;
    function scheduleInject() {
        if (scheduled) return;
        scheduled = setTimeout(function () {
            scheduled = null;
            injectAll();
        }, 200);
    }

    injectAll();
    var observer = new MutationObserver(function (mutations) {
        // Only react to child-list changes; skip our own ids to avoid loops.
        for (var i = 0; i < mutations.length; i++) {
            var added = mutations[i].addedNodes;
            for (var k = 0; k < added.length; k++) {
                var node = added[k];
                if (node.nodeType !== 1) continue;
                var el = /** @type {Element} */ (node);
                var id = el.id || '';
                if (ENTRY_IDS.indexOf(id) >= 0) continue;
                scheduleInject();
                return;
            }
        }
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });
})();
