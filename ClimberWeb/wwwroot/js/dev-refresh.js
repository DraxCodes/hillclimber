// Development only: reload the open tab when the running host changes.
//
// The stamp endpoint 404s outside Development, so this file is inert in production. Two things it
// gets right that a naive "reload on save" does not:
//
//  - A save can make `dotnet watch` restart the host, which takes a few seconds. Reloading into
//    that window lands the tab on a refused connection. A changed pid means a restart has already
//    happened and the new process is answering, so that case reloads immediately.
//  - A hot patch does not change the pid, so that case waits until the sources have been quiet for
//    long enough that watch has decided whether it needs a restart.
//
// A reload is postponed while the user is typing and while a dialog is open, so a save can never
// throw away a half-written prompt.
(function () {
    var KEY_URL = "/__dev/stamp";
    var STORAGE_KEY = "climber-dev-stamp";
    var POLL_MS = 1000;
    var SETTLE_MS = 3000;
    var RESTART_GRACE_MS = 300;

    // Remembered across reloads: the tab's own reload must not become the new baseline, or a save
    // that lands moments later would be missed.
    var seen = recall();
    var wantReload = false;
    var reloadAt = 0;

    function recall() {
        try {
            var raw = sessionStorage.getItem(STORAGE_KEY);
            return raw ? JSON.parse(raw) : null;
        } catch (e) {
            return null;
        }
    }

    function remember(value) {
        try { sessionStorage.setItem(STORAGE_KEY, JSON.stringify(value)); }
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
        return [stamp.build, stamp.source].join("|");
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

        var now = Date.now();
        var key = keyOf(stamp);

        if (seen === null) {
            seen = { key: key, pid: stamp.pid };
            remember(seen);
            return;
        }

        if (stamp.pid !== seen.pid) {
            // A restart already happened and the new process just answered: safe to reload soon.
            seen = { key: key, pid: stamp.pid };
            remember(seen);
            wantReload = true;
            reloadAt = now + RESTART_GRACE_MS;
        } else if (key !== seen.key) {
            seen.key = key;
            remember(seen);
            wantReload = true;
            reloadAt = now + SETTLE_MS;
        }

        if (!wantReload || now < reloadAt || focusedOnInput()) return;

        wantReload = false;
        location.reload();
    }

    tick();
    setInterval(tick, POLL_MS);
})();