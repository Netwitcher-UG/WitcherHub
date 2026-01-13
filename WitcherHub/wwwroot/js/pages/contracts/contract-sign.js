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
    const customerDate = $("customerDate");
    const customerName = $("customerName");

    const canvas = $("sigCanvas");
    if (!chkAgree || !btnOpen || !sigModal || !canvas || !customerName) return;

    const ctx = canvas.getContext("2d");

    const STORAGE_SIG = "fekrahub.contract.signature";
    const STORAGE_AT = "fekrahub.contract.signedAt";
    const STORAGE_NAME = "fekrahub.contract.customerName";

    function formatSignedAt(iso) {
        try {
            const d = new Date(iso);
            return d.toLocaleString(document.documentElement.lang || undefined, {
                year: "numeric", month: "2-digit", day: "2-digit",
                hour: "2-digit", minute: "2-digit"
            });
        } catch { return iso; }
    }
    function formatDateOnly(iso) {
        try {
            const d = new Date(iso);
            return d.toLocaleDateString(document.documentElement.lang || undefined, {
                year: "numeric", month: "2-digit", day: "2-digit"
            });
        } catch { return ""; }
    }

    function hideToast() {
        if (!toast) return;
        toast.style.display = "none";
        toast.textContent = "";
        toast.classList.remove("error");
    }
    function decodeHtmlEntities(s) {
        if (!s) return s;
        const t = document.createElement("textarea");
        t.innerHTML = s;
        return t.value;
    }

    function showToast(msg, isError) {
        if (!toast) return;

        msg = decodeHtmlEntities(msg);

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

    function canSignNow() {
        const nameOk = (customerName.value || "").trim().length > 0;
        return chkAgree.checked && nameOk;
    }

    function refreshSignButton() {
        btnOpen.disabled = !canSignNow();
    }

    function setSignedUI(whenIso, dataUrl) {
        if (sigImage) {
            sigImage.src = dataUrl;
            sigImage.style.display = "block";
        }
        if (sigPlaceholder) sigPlaceholder.style.display = "none";

        if (signedAt) signedAt.textContent = formatSignedAt(whenIso);
        if (customerDate) customerDate.textContent = formatDateOnly(whenIso);

        if (signedBadge) {
            signedBadge.textContent = i18n.signed || "Signed";
            signedBadge.classList.add("signed");
        }

        // lock fields after sign (production)
        customerName.readOnly = true;
        chkAgree.disabled = true;
        btnOpen.disabled = true;
    }

    function setUnsignedUI() {
        if (sigImage) {
            sigImage.src = "";
            sigImage.style.display = "none";
        }
        if (sigPlaceholder) sigPlaceholder.style.display = "block";

        if (signedAt) signedAt.textContent = "";
        if (customerDate) customerDate.textContent = "";

        if (signedBadge) {
            signedBadge.textContent = i18n.unsigned || "Unsigned";
            signedBadge.classList.remove("signed");
        }

        customerName.readOnly = false;
        chkAgree.disabled = false;
        refreshSignButton();
    }

    function resizeCanvas() {
        const ratio = window.devicePixelRatio || 1;
        const rect = canvas.getBoundingClientRect();
        const w = Math.max(1, Math.floor(rect.width));
        const h = Math.max(1, Math.floor(rect.height));

        canvas.width = Math.floor(w * ratio);
        canvas.height = Math.floor(h * ratio);

        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);

        // ✅ darker + thicker signature
        ctx.lineWidth = 3.6;
        ctx.lineCap = "round";
        ctx.lineJoin = "round";
        ctx.strokeStyle = "#111";
        ctx.globalAlpha = 1;
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
            showToast(i18n.mustAgree || "Please confirm agreement before signing", true);
            return;
        }

        const name = (customerName?.value || "").trim();
        if (!name) {
            showToast(i18n.fillName || "Please enter your name before signing", true);
            customerName?.focus();
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

    // Events
    chkAgree.addEventListener("change", () => {
        refreshSignButton();
        if (chkAgree.checked) hideToast();
    });

    customerName.addEventListener("input", () => {
        localStorage.setItem(STORAGE_NAME, customerName.value || "");
        refreshSignButton();
    });

    btnOpen.addEventListener("click", openModal);
    sigBoxClick?.addEventListener("click", openModal);
    sigBoxClick?.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            openModal();
        }
    });

    btnClose?.addEventListener("click", closeModal);
    btnCancel?.addEventListener("click", closeModal);

    sigModal.addEventListener("click", (e) => {
        if (e.target === sigModal) closeModal();
    });

    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape" && sigModal.style.display === "block") closeModal();
    });

    // Drawing
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
        localStorage.removeItem(STORAGE_NAME);

        customerName.value = "";
        chkAgree.checked = false;

        setUnsignedUI();
        hideToast();
        clearCanvas();
    });

    window.addEventListener("resize", () => {
        if (sigModal.style.display === "block") resizeCanvas();
    });

    function initFromStorage() {
        const savedName = localStorage.getItem(STORAGE_NAME);
        if (savedName) customerName.value = savedName;

        const savedSig = localStorage.getItem(STORAGE_SIG);
        const savedAt = localStorage.getItem(STORAGE_AT);

        if (savedSig && savedAt) {
            // already signed
            chkAgree.checked = true;
            setSignedUI(savedAt, savedSig);
        } else {
            setUnsignedUI();
        }

        refreshSignButton();
    }

    resizeCanvas();
    clearCanvas();
    initFromStorage();
})();
