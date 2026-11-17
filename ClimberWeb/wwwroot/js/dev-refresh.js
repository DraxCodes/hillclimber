// Development only: reload the open tab when the running host changes.
//
// The stamp endpoint 404s outside Development, so this file is inert in production. A reload
// is postponed while the user is typing and while a dialog is open, so a save can never throw
// away a half-written prompt.
(function () {
    var KEY_URL = "/__dev/stamp";
    var STORAGE_KEY = "climber-dev-stamp";
    var POLL_MS = 1000;
    var SETTLE_MS = 1200;

    // Remembered across reloads: the tab's own reload must not become the new baseline, or a
    // save that lands moments later would be missed.
    var lastKey = recall();
    var wantReload = false;
    var reloadAt = 0;

    function recall() {
        try { return sessionStorage.getItem(STORAGE_KEY); }
        catch (e) { return null; }
    }

    function remember(key) {
        try { sessionStorage.setItem(STORAGE_KEY, key); }
        catch (e) { }
    }

    function focusedOnInput() {
        var el = document.activeElement;
        if (!el) return false;

        var tag = (el.tagName || "").toLowerCase();
        if (tag === "input" || tag === "textarea" || tag === "select" || el.isContentEditable) {
            return true;
        }

        return document.querySelector('[role="dialog"]:not([hidden])') !== null;
    }

    function keyOf(stamp) {
        return [stamp.pid, stamp.build, stamp.source].join("|");
    }

    async function tick() {
        var stamp;
        try {
            var response = await fetch(KEY_URL, { cache: "no-store" });
            if (!response.ok) return;
            stamp = await response.json();
        } catch (e) {
            // The host is restarting. Try again on the next tick rather than reloading into a
            // refused connection.
            return;
        }

        var key = keyOf(stamp);
        if (lastKey === null) {
            lastKey = key;
            remember(key);
            return;
        }

        if (key !== lastKey) {
            lastKey = key;
            remember(key);
            wantReload = true;
            reloadAt = Date.now() + SETTLE_MS;
        }

        if (!wantReload || Date.now() < reloadAt || focusedOnInput()) return;

        wantReload = false;
        location.reload();
    }

    tick();
    setInterval(tick, POLL_MS);
})();