// wwwroot/js/pages/contracts/contract-sign.js

(function () {
    const i18n = window.contractPageI18n || {};

    const $ = (id) => document.getElementById(id);

    const chkAgree = $("chkAgree");
    const btnOpen = $("btnOpen");
    const btnPrint = $("btnPrint");
    const btnReset = $("btnReset");

    const sigBoxClick = $("sigBoxClick");
    const toast = $("toast");

    const sigModal = $("sigModal");
    const btnClose = $("btnClose");
    const btnCancel = $("btnCancel");
    const btnClear = $("btnClear");
    const btnAccept = $("btnAccept");
    const modalStatus = $("modalStatus");

    const signedBadge = $("signedBadge");
    const sigImage = $("sigImage");
    const sigPlaceholder = $("sigPlaceholder");
    const signedAt = $("signedAt");

    const canvas = $("sigCanvas");
    if (!chkAgree || !btnOpen || !sigModal || !canvas) return;

    const ctx = canvas.getContext("2d");

    const STORAGE_SIG = "fekrahub.contract.signature";
    const STORAGE_AT = "fekrahub.contract.signedAt";

    function lang2() {
        return (document.documentElement.lang || "en").slice(0, 2).toLowerCase();
    }

    function formatSignedAt(iso) {
        try {
            const d = new Date(iso);
            return d.toLocaleString(document.documentElement.lang || undefined, {
                year: "numeric",
                month: "2-digit",
                day: "2-digit",
                hour: "2-digit",
                minute: "2-digit"
            });
        } catch {
            return iso;
        }
    }

    function hideToast() {
        if (!toast) return;
        toast.style.display = "none";
        toast.textContent = "";
        toast.classList.remove("error");
    }

    function showToast(msg, isError) {
        if (!toast) return;
        if (!msg) {
            hideToast();
            return;
        }
        toast.textContent = msg;
        toast.classList.toggle("error", !!isError);
        toast.style.display = "block";
        clearTimeout(showToast._t);
        showToast._t = setTimeout(hideToast, 2600);
    }

    function showModalStatus(msg, isError) {
        if (!modalStatus) return;
        if (!msg) {
            modalStatus.style.display = "none";
            modalStatus.textContent = "";
            return;
        }
        modalStatus.textContent = msg;
        modalStatus.style.display = "block";
        modalStatus.style.borderColor = isError ? "rgba(220,53,69,.25)" : "rgba(0,0,0,.10)";
        modalStatus.style.background = isError ? "#fff5f5" : "#fafafa";
    }

    function setSignedUI(whenIso, dataUrl) {
        if (sigImage) {
            sigImage.src = dataUrl;
            sigImage.style.display = "block";
        }
        if (sigPlaceholder) sigPlaceholder.style.display = "none";
        if (signedAt) signedAt.textContent = formatSignedAt(whenIso);

        if (signedBadge) {
            signedBadge.textContent = i18n.signed || "Signed";
            signedBadge.classList.add("signed");
        }
    }

    function setUnsignedUI() {
        if (sigImage) {
            sigImage.src = "";
            sigImage.style.display = "none";
        }
        if (sigPlaceholder) sigPlaceholder.style.display = "block";
        if (signedAt) signedAt.textContent = "—";

        if (signedBadge) {
            signedBadge.textContent = i18n.unsigned || "Unsigned";
            signedBadge.classList.remove("signed");
        }
    }

    function resizeCanvas() {
        const ratio = window.devicePixelRatio || 1;
        const rect = canvas.getBoundingClientRect();
        const w = Math.max(1, Math.floor(rect.width));
        const h = Math.max(1, Math.floor(rect.height));
        canvas.width = Math.floor(w * ratio);
        canvas.height = Math.floor(h * ratio);
        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        ctx.lineWidth = 2;
        ctx.lineCap = "round";
        ctx.lineJoin = "round";
    }

    function clearCanvas() {
        const rect = canvas.getBoundingClientRect();
        ctx.clearRect(0, 0, rect.width, rect.height);
    }

    function isBlank() {
        const rect = canvas.getBoundingClientRect();
        const w = Math.max(1, Math.floor(rect.width));
        const h = Math.max(1, Math.floor(rect.height));
        const data = ctx.getImageData(0, 0, w, h).data;
        for (let i = 3; i < data.length; i += 4) {
            if (data[i] !== 0) return false;
        }
        return true;
    }

    function openModal() {
        if (!chkAgree.checked) {
            showToast(i18n.mustAgree || "Please confirm agreement.", true);
            return;
        }
        sigModal.style.display = "block";
        sigModal.setAttribute("aria-hidden", "false");
        document.body.style.overflow = "hidden";
        resizeCanvas();
        clearCanvas();
        showModalStatus("", false);
        canvas.focus?.();
    }

    function closeModal() {
        sigModal.style.display = "none";
        sigModal.setAttribute("aria-hidden", "true");
        document.body.style.overflow = "";
        showModalStatus("", false);
    }

    function wireLangButtons() {
        const current = lang2();
        document.querySelectorAll(".contractPage__langBtn").forEach((b) => {
            if (b.dataset.culture === current) b.classList.add("active");
            b.addEventListener("click", () => {
                const url = new URL(window.location.href);
                url.searchParams.set("culture", b.dataset.culture);
                url.searchParams.set("ui-culture", b.dataset.culture);
                window.location.href = url.toString();
            });
        });
    }

    chkAgree.addEventListener("change", () => {
        btnOpen.disabled = !chkAgree.checked;
        if (chkAgree.checked) hideToast();
    });

    btnOpen.addEventListener("click", openModal);
    sigBoxClick?.addEventListener("click", openModal);

    btnClose?.addEventListener("click", closeModal);
    btnCancel?.addEventListener("click", closeModal);

    sigModal.addEventListener("click", (e) => {
        if (e.target === sigModal) closeModal();
    });

    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape" && sigModal.style.display === "block") closeModal();
    });

    let drawing = false;
    let last = null;

    function getPoint(e) {
        const rect = canvas.getBoundingClientRect();
        return { x: e.clientX - rect.left, y: e.clientY - rect.top };
    }

    canvas.addEventListener("pointerdown", (e) => {
        drawing = true;
        canvas.setPointerCapture(e.pointerId);
        last = getPoint(e);
    });

    canvas.addEventListener("pointermove", (e) => {
        if (!drawing) return;
        const p = getPoint(e);
        ctx.beginPath();
        ctx.moveTo(last.x, last.y);
        ctx.lineTo(p.x, p.y);
        ctx.stroke();
        last = p;
    });

    canvas.addEventListener("pointerup", (e) => {
        drawing = false;
        last = null;
        try { canvas.releasePointerCapture(e.pointerId); } catch { }
    });

    canvas.addEventListener("pointercancel", () => {
        drawing = false;
        last = null;
    });

    btnClear?.addEventListener("click", () => {
        clearCanvas();
        showModalStatus(i18n.cleared || "Cleared.", false);
    });

    btnAccept?.addEventListener("click", () => {
        if (isBlank()) {
            showModalStatus(i18n.emptyCanvas || "Please sign first.", true);
            return;
        }

        const dataUrl = canvas.toDataURL("image/png");
        const whenIso = new Date().toISOString();

        localStorage.setItem(STORAGE_SIG, dataUrl);
        localStorage.setItem(STORAGE_AT, whenIso);

        setSignedUI(whenIso, dataUrl);
        showToast(i18n.signedOk || "Signed successfully ✅", false);
        closeModal();
    });

    btnPrint?.addEventListener("click", () => window.print());

    btnReset?.addEventListener("click", () => {
        localStorage.removeItem(STORAGE_SIG);
        localStorage.removeItem(STORAGE_AT);

        setUnsignedUI();
        chkAgree.checked = false;
        btnOpen.disabled = true;
        clearCanvas();
        hideToast();
        showModalStatus("", false);
    });

    window.addEventListener("resize", () => {
        if (sigModal.style.display === "block") resizeCanvas();
    });

    function initFromStorage() {
        const savedSig = localStorage.getItem(STORAGE_SIG);
        const savedAt = localStorage.getItem(STORAGE_AT);
        if (savedSig && savedAt) {
            chkAgree.checked = true;
            btnOpen.disabled = false;
            setSignedUI(savedAt, savedSig);
        } else {
            setUnsignedUI();
            btnOpen.disabled = !chkAgree.checked;
        }
    }

    wireLangButtons();
    resizeCanvas();
    clearCanvas();
    initFromStorage();
})();
