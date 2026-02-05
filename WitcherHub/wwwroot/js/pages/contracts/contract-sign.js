// wwwroot/js/pages/contracts/contract-sign.js
(function () {
    const i18n = window.contractPageI18n || {};
    const serverState = window.contractServerState || {};
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
    const customerEmail = $("customerEmail");

    const canvas = $("sigCanvas");
    const signEndpoint = $("signEndpoint");

    if (!chkAgree || !btnOpen || !sigModal || !canvas || !customerName || !signEndpoint) return;

    const ctx = canvas.getContext("2d");
    function setCulture(culture) {
        document.cookie = `.AspNetCore.Culture=c=${culture}|uic=${culture}; path=/; samesite=lax`;
        location.reload();
    }

    document.querySelectorAll(".contractPage__langBtn").forEach(btn => {
        btn.addEventListener("click", () => {
            const c = btn.getAttribute("data-culture");
            if (c) setCulture(c);
        });
    });

    function formatSignedAt(iso) {
        try {
            const d = new Date(iso);
            return new Intl.DateTimeFormat("de-DE", {
                year: "numeric", month: "2-digit", day: "2-digit",
                hour: "2-digit", minute: "2-digit"
            }).format(d);
        } catch { return iso; }
    }

    function formatDateOnly(iso) {
        try {
            const d = new Date(iso);
            return new Intl.DateTimeFormat("de-DE", {
                year: "numeric", month: "2-digit", day: "2-digit"
            }).format(d);
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
        const emailOk = (customerEmail?.value || "").trim().length > 0;
        return chkAgree.checked && nameOk && emailOk && !serverState.isSigned;
    }

    function refreshSignButton() {
        btnOpen.disabled = !canSignNow();
    }

    function setSignedUI(whenIso, dataUrl) {
        if (sigImage && dataUrl) {
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

        // lock everything
        customerName.readOnly = true;
        if (customerEmail) customerEmail.readOnly = true;

        chkAgree.checked = true;
        chkAgree.disabled = true;

        if (btnOpen) btnOpen.style.display = "none";
        if (btnReset) btnReset.style.display = "none";

        // prevent modal open
        if (sigBoxClick) {
            sigBoxClick.style.pointerEvents = "none";
            sigBoxClick.setAttribute("aria-disabled", "true");
        }

        serverState.isSigned = true;
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
        if (customerEmail) customerEmail.readOnly = false;

        chkAgree.disabled = false;

        if (btnOpen) btnOpen.style.display = "";
        if (btnReset) btnReset.style.display = "";

        if (sigBoxClick) {
            sigBoxClick.style.pointerEvents = "";
            sigBoxClick.removeAttribute("aria-disabled");
        }

        serverState.isSigned = false;
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
        if (serverState.isSigned) return;

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

        const email = (customerEmail?.value || "").trim();
        if (!email) {
            showToast(i18n.fillEmail || "Please enter your email before signing", true);
            customerEmail?.focus();
            return;
        }

        if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
            showToast(i18n.invalidEmail || "Please enter a valid email address", true);
            customerEmail?.focus();
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

    function getAntiForgeryToken() {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : "";
    }

    async function postSignatureToServer(dataUrl) {
        const token = getAntiForgeryToken();
        const fd = new FormData();
        fd.append("SignerName", (customerName.value || "").trim());
        fd.append("SignerEmail", (customerEmail?.value || "").trim());
        fd.append("SignatureDataUrl", dataUrl);

        const res = await fetch(signEndpoint.value, {
            method: "POST",
            headers: token ? { "RequestVerificationToken": token } : {},
            body: fd
        });

        let json = null;
        try { json = await res.json(); } catch { }

        if (!res.ok || !json || json.ok !== true) {
            const code = json && json.code ? json.code : "";
            const msg =
                (code === "FIELDS_REQUIRED") ? (i18n.fillFields || "Please fill name and email before signing") :
                    (code === "INVALID_EMAIL") ? (i18n.invalidEmail || "Please enter a valid email address") :
                        (json && json.message) ? json.message :
                            ("HTTP " + res.status);

            throw new Error(msg);
        }


        return json;
    }

    // Events
    chkAgree.addEventListener("change", () => {
        refreshSignButton();
        if (chkAgree.checked) hideToast();
    });

    customerName.addEventListener("input", refreshSignButton);
    if (customerEmail) customerEmail.addEventListener("input", refreshSignButton);

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

    btnAccept?.addEventListener("click", async () => {
        if (isBlank()) {
            showModalStatus(i18n.emptyCanvas || "Please sign first.", true);
            return;
        }

        const dataUrl = canvas.toDataURL("image/png");

        try {
            btnAccept.disabled = true;
            showModalStatus("Saving...", false);

            const result = await postSignatureToServer(dataUrl);
            const whenIso = result.signedAtIso || new Date().toISOString();

            setSignedUI(whenIso, dataUrl);
            showToast(i18n.signedOk || "Signed successfully ✅", false);
            closeModal();
        } catch (err) {
            showModalStatus(err.message || "Failed to save signature.", true);
        } finally {
            btnAccept.disabled = false;
        }
    });

    btnPrint?.addEventListener("click", () => window.print());

    // Reset is UI-only now (does NOT delete from DB)
    btnReset?.addEventListener("click", () => {
        if (serverState.isSigned) return;

        // restore prefill from server
        customerName.value = serverState.signerName || "";
        if (customerEmail) customerEmail.value = serverState.signerEmail || "";

        chkAgree.checked = false;
        setUnsignedUI();
        hideToast();
        clearCanvas();
    });

    window.addEventListener("resize", () => {
        if (sigModal.style.display === "block") resizeCanvas();
    });

    function initFromServer() {
        // Prefill fields
        if (serverState.signerName && !customerName.value) customerName.value = serverState.signerName;
        if (customerEmail && serverState.signerEmail && !customerEmail.value) customerEmail.value = serverState.signerEmail;

        if (serverState.isSigned && serverState.signatureDataUrl && serverState.signedAtIso) {
            chkAgree.checked = true;
            setSignedUI(serverState.signedAtIso, serverState.signatureDataUrl);
        } else {
            setUnsignedUI();
        }

        refreshSignButton();
    }

    resizeCanvas();
    clearCanvas();
    initFromServer();
})();
