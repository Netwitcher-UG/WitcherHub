(function () {
    const globalName = "forcedBackNavigation";

    if (window[globalName]) return;

    window[globalName] = {
        bind: function (backUrl, stateKey) {
            if (!backUrl) return;

            const key = stateKey || "__forcedBackRedirect";
            const listenerKey = "__forcedBackListener_" + key;

            function arm() {
                const currentState = history.state || {};

                if (currentState[key] === true) return;

                const nextState = Object.assign({}, currentState, { [key]: true });
                history.pushState(nextState, "", location.href);
            }

            arm();

            window.addEventListener("pageshow", arm);

            if (window[listenerKey]) {
                window.removeEventListener("popstate", window[listenerKey]);
            }

            const onPopState = function () {
                window.location.replace(backUrl);
            };

            window[listenerKey] = onPopState;
            window.addEventListener("popstate", onPopState);
        }
    };
})();