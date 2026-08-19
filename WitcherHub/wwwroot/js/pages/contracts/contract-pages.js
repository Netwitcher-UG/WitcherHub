/*
    Shows a contract as the pages it will print on.

    The preview was one sheet of unbounded height with `min-height: 297mm` on it,
    which made a one-page contract look like a page and a twelve-page contract
    look like a very long receipt. Nothing on the screen told you where a page
    ended, how many there were, or whether a signature block was about to be split
    across two of them.

    Nothing here changes the document. No scaling, no shrink-to-fit, no font
    adjustment, no clipping: the paper keeps its real A4 width and its real
    margins, and this only draws where the page breaks fall and counts them. A
    contract is as long as it is — the job is to show that honestly, not to make
    it fit.

    The measurements come from the element itself rather than from constants, so
    a browser that rounds millimetres differently still gets its own boundaries
    right.
*/
(function () {
    "use strict";

    var PAGE_HEIGHT_MM = 297;

    function mmToPx(paper) {
        // A one-millimetre probe, measured in place, so the ratio is whatever this
        // browser actually uses rather than an assumed 96dpi.
        var probe = document.createElement("div");
        probe.style.cssText = "position:absolute;visibility:hidden;height:100mm;width:0";
        paper.appendChild(probe);
        var px = probe.getBoundingClientRect().height / 100;
        paper.removeChild(probe);
        return px;
    }

    function paginate(paper) {
        var overlay = paper.querySelector(".contractPaper__pages");

        if (!overlay) {
            overlay = document.createElement("div");
            overlay.className = "contractPaper__pages";
            overlay.setAttribute("aria-hidden", "true");
            paper.appendChild(overlay);
        }

        var px = mmToPx(paper);
        if (!px || !isFinite(px)) return;

        var pageHeight = PAGE_HEIGHT_MM * px;

        // The overlay is out of flow, so the paper's own height is the content's.
        overlay.innerHTML = "";

        var content = paper.scrollHeight;
        var pages = Math.max(1, Math.ceil((content - 1) / pageHeight));

        // The sheet ends on a page boundary. Without this the last page stops
        // wherever the text does, which reads as a page that got cut off.
        paper.style.minHeight = (pages * PAGE_HEIGHT_MM) + "mm";

        for (var i = 1; i <= pages; i++) {
            var label = document.createElement("div");
            label.className = "contractPaper__pageNo";
            label.style.top = (i * PAGE_HEIGHT_MM) + "mm";
            label.textContent = "Seite " + i + " von " + pages;
            overlay.appendChild(label);

            if (i < pages) {
                var edge = document.createElement("div");
                edge.className = "contractPaper__pageEdge";
                edge.style.top = (i * PAGE_HEIGHT_MM) + "mm";
                overlay.appendChild(edge);
            }
        }

        paper.dataset.pageCount = String(pages);

        var counter = document.querySelector("[data-contract-page-count]");
        if (counter) {
            counter.textContent = pages === 1 ? "1 Seite" : pages + " Seiten";
        }
    }

    function start() {
        var papers = document.querySelectorAll(".contractPage__paper");
        if (!papers.length) return;

        papers.forEach(function (paper) {
            // Measured after the fonts land: a serif face that arrives late
            // reflows the text and moves every page boundary with it.
            var run = function () { paginate(paper); };

            run();

            if (document.fonts && document.fonts.ready) {
                document.fonts.ready.then(run).catch(function () { });
            }

            // The preview is inside a responsive column, so a window change moves
            // the boundaries. Debounced — this measures layout.
            var pending = null;
            window.addEventListener("resize", function () {
                window.clearTimeout(pending);
                pending = window.setTimeout(run, 150);
            });

            // The editor rewrites the document in place as it is typed.
            var body = paper.querySelector(".contractPage__contractHtml");
            if (body && window.MutationObserver) {
                var settle = null;
                new MutationObserver(function () {
                    window.clearTimeout(settle);
                    settle = window.setTimeout(run, 120);
                }).observe(body, { childList: true, subtree: true, characterData: true });
            }
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", start);
    } else {
        start();
    }
})();
