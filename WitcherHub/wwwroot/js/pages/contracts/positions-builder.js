// Contract position builder.
//
// Positions live in this array until the user saves. A position may reference a
// catalog service or stand alone as a manual entry — a manual position keeps
// catalogServiceId null rather than pointing at a placeholder record.
//
// Every total shown here is a preview. The server recalculates on save and its
// answer wins.
(function () {
    "use strict";

    const app = document.getElementById("positionsApp");
    if (!app) return;

    const contractId = app.dataset.contractId;
    const defaultCurrency = app.dataset.currency || "EUR";

    const listEl = document.getElementById("positionList");
    const emptyEl = document.getElementById("emptyState");

    const catalog = readJson("catalogServices") || [];
    let positions = (readJson("initialPositions") || []).map(normalise);
    let organized = null;
    let dirty = false;
    let busy = false;

    const money = new Intl.NumberFormat("de-DE", { style: "currency", currency: defaultCurrency });

    const BILLING_CYCLES = ["OneTime", "Monthly", "Quarterly", "SemiAnnual", "Annual"];
    const PRICING_MODELS = ["Fixed", "Unit", "Hourly", "Tiered"];
    const ACTIVATION = ["NotApplicable", "AfterSignature", "AfterInitialPayment", "OnSpecifiedDate", "ManualActivation"];

    function readJson(id) {
        const el = document.getElementById(id);
        if (!el) return null;
        try { return JSON.parse(el.textContent); } catch { return null; }
    }

    function normalise(p) {
        return Object.assign({
            clientId: cryptoId(),
            contractItemId: null,
            sourceType: "Manual",
            catalogServiceId: null,
            position: 1,
            title: "",
            serviceType: "",
            description: "",
            scope: "",
            deliverables: [],
            quantity: 1,
            unit: "",
            pricingModel: "Fixed",
            unitPrice: null,
            currency: defaultCurrency,
            vatRate: 19,
            discountType: null,
            discountValue: null,
            billingCycle: "OneTime",
            durationPeriods: null,
            isFree: false,
            deliveryMethod: "",
            activationMethod: "NotApplicable",
            startDate: null,
            deliveryDate: null,
            acceptanceCriteria: [],
            customerResponsibilities: [],
            assumptions: [],
            exclusions: [],
            notes: ""
        }, p || {});
    }

    function cryptoId() {
        return (window.crypto && window.crypto.randomUUID)
            ? window.crypto.randomUUID().replace(/-/g, "")
            : String(Date.now()) + Math.random().toString(16).slice(2);
    }

    // Accepts German and invariant notation, matching the server-side binder.
    function parseNumber(value) {
        if (value === null || value === undefined || value === "") return null;
        if (typeof window.witcherhubParseDecimal === "function") {
            const n = window.witcherhubParseDecimal(value);
            return isNaN(n) ? null : n;
        }
        const n = parseFloat(String(value).replace(/\./g, "").replace(",", "."));
        return isNaN(n) ? null : n;
    }

    function lineNet(p) {
        if (p.isFree) return 0;
        const unit = Number(p.unitPrice) || 0;
        const qty = Number(p.quantity) > 0 ? Number(p.quantity) : 0;
        const gross = p.pricingModel === "Fixed" ? unit : unit * qty;
        let discount = 0;
        if (p.discountType === "Percent") discount = gross * ((Number(p.discountValue) || 0) / 100);
        else if (p.discountType === "Amount" || p.discountType === "Fixed") discount = Number(p.discountValue) || 0;
        return Math.max(0, gross - Math.min(Math.max(discount, 0), gross));
    }

    // ---------------------------------------------------------------- render

    function render() {
        listEl.innerHTML = "";
        emptyEl.classList.toggle("d-none", positions.length > 0);

        positions.forEach((p, index) => {
            p.position = index + 1;
            listEl.appendChild(card(p, index));
        });

        updateTotals();
    }

    function card(p, index) {
        const wrap = document.createElement("div");
        wrap.className = "border rounded-3 p-3";
        wrap.dataset.clientId = p.clientId;

        const badge = p.sourceType === "Manual"
            ? '<span class="badge bg-primary-subtle text-primary-emphasis">Manual</span>'
            : '<span class="badge bg-secondary-subtle text-secondary-emphasis">Catalog</span>';

        wrap.innerHTML = `
          <div class="d-flex flex-wrap align-items-center gap-2 mb-3">
            <span class="fw-bold">#${index + 1}</span>
            ${badge}
            <span class="ms-auto btn-group btn-group-sm">
              <button type="button" class="btn btn-outline-secondary" data-op="up"        title="Move up"    aria-label="Move up">&uarr;</button>
              <button type="button" class="btn btn-outline-secondary" data-op="down"      title="Move down"  aria-label="Move down">&darr;</button>
              <button type="button" class="btn btn-outline-secondary" data-op="duplicate" title="Duplicate"  aria-label="Duplicate">&#128203;</button>
              <button type="button" class="btn btn-outline-danger"    data-op="delete"    title="Delete"     aria-label="Delete">&times;</button>
            </span>
          </div>

          <div class="row g-2">
            <div class="col-12 col-md-6">
              <label class="form-label small">Title *</label>
              <input class="form-control form-control-sm" data-f="title" value="${esc(p.title)}" />
            </div>
            <div class="col-6 col-md-3">
              <label class="form-label small">Service type</label>
              <input class="form-control form-control-sm" data-f="serviceType" value="${esc(p.serviceType)}" />
            </div>
            <div class="col-6 col-md-3">
              <label class="form-label small">Unit</label>
              <input class="form-control form-control-sm" data-f="unit" value="${esc(p.unit)}" />
            </div>

            <div class="col-12">
              <label class="form-label small">Description</label>
              <textarea class="form-control form-control-sm" rows="2" data-f="description">${esc(p.description)}</textarea>
            </div>
            <div class="col-12">
              <label class="form-label small">Scope of work</label>
              <textarea class="form-control form-control-sm" rows="2" data-f="scope">${esc(p.scope)}</textarea>
            </div>
            <div class="col-12">
              <label class="form-label small">Deliverables <span class="text-secondary">(one per line)</span></label>
              <textarea class="form-control form-control-sm" rows="2" data-f="deliverables">${esc((p.deliverables || []).join("\n"))}</textarea>
            </div>

            <div class="col-6 col-md-2">
              <label class="form-label small">Quantity</label>
              <input class="form-control form-control-sm" data-f="quantity" inputmode="decimal" value="${p.quantity ?? 1}" />
            </div>
            <div class="col-6 col-md-3">
              <label class="form-label small">Pricing model</label>
              <select class="form-select form-select-sm" data-f="pricingModel">
                ${PRICING_MODELS.map(m => `<option value="${m}"${p.pricingModel === m ? " selected" : ""}>${m}</option>`).join("")}
              </select>
            </div>
            <div class="col-6 col-md-3">
              <label class="form-label small">Unit price</label>
              <input class="form-control form-control-sm" data-f="unitPrice" inputmode="decimal"
                     value="${p.unitPrice ?? ""}" ${p.isFree ? "disabled" : ""} />
            </div>
            <div class="col-3 col-md-2">
              <label class="form-label small">VAT %</label>
              <input class="form-control form-control-sm" data-f="vatRate" inputmode="decimal" value="${p.vatRate ?? ""}" />
            </div>
            <div class="col-3 col-md-2 d-flex align-items-end">
              <div class="form-check">
                <input class="form-check-input" type="checkbox" data-f="isFree" id="free-${p.clientId}" ${p.isFree ? "checked" : ""} />
                <label class="form-check-label small" for="free-${p.clientId}">Free</label>
              </div>
            </div>

            <div class="col-6 col-md-3">
              <label class="form-label small">Discount type</label>
              <select class="form-select form-select-sm" data-f="discountType">
                <option value="">None</option>
                <option value="Percent"${p.discountType === "Percent" ? " selected" : ""}>Percent</option>
                <option value="Amount"${p.discountType === "Amount" ? " selected" : ""}>Amount</option>
              </select>
            </div>
            <div class="col-6 col-md-3">
              <label class="form-label small">Discount value</label>
              <input class="form-control form-control-sm" data-f="discountValue" inputmode="decimal" value="${p.discountValue ?? ""}" />
            </div>
            <div class="col-6 col-md-3">
              <label class="form-label small">Billing cycle</label>
              <select class="form-select form-select-sm" data-f="billingCycle">
                ${BILLING_CYCLES.map(c => `<option value="${c}"${p.billingCycle === c ? " selected" : ""}>${c}</option>`).join("")}
              </select>
            </div>
            <div class="col-6 col-md-3">
              <label class="form-label small">Periods</label>
              <input class="form-control form-control-sm" data-f="durationPeriods" inputmode="numeric" value="${p.durationPeriods ?? ""}" />
            </div>

            <div class="col-12 col-md-4">
              <label class="form-label small">Activation</label>
              <select class="form-select form-select-sm" data-f="activationMethod">
                ${ACTIVATION.map(a => `<option value="${a}"${p.activationMethod === a ? " selected" : ""}>${a}</option>`).join("")}
              </select>
            </div>
            <div class="col-6 col-md-4">
              <label class="form-label small">Start date</label>
              <input type="date" class="form-control form-control-sm" data-f="startDate" value="${p.startDate ?? ""}" />
            </div>
            <div class="col-6 col-md-4">
              <label class="form-label small">Delivery date</label>
              <input type="date" class="form-control form-control-sm" data-f="deliveryDate" value="${p.deliveryDate ?? ""}" />
            </div>

            <div class="col-12">
              <details>
                <summary class="small text-secondary">Acceptance, responsibilities, assumptions, exclusions</summary>
                <div class="row g-2 mt-1">
                  <div class="col-12 col-md-6">
                    <label class="form-label small">Acceptance criteria</label>
                    <textarea class="form-control form-control-sm" rows="2" data-f="acceptanceCriteria">${esc((p.acceptanceCriteria || []).join("\n"))}</textarea>
                  </div>
                  <div class="col-12 col-md-6">
                    <label class="form-label small">Customer responsibilities</label>
                    <textarea class="form-control form-control-sm" rows="2" data-f="customerResponsibilities">${esc((p.customerResponsibilities || []).join("\n"))}</textarea>
                  </div>
                  <div class="col-12 col-md-6">
                    <label class="form-label small">Assumptions</label>
                    <textarea class="form-control form-control-sm" rows="2" data-f="assumptions">${esc((p.assumptions || []).join("\n"))}</textarea>
                  </div>
                  <div class="col-12 col-md-6">
                    <label class="form-label small">Exclusions</label>
                    <textarea class="form-control form-control-sm" rows="2" data-f="exclusions">${esc((p.exclusions || []).join("\n"))}</textarea>
                  </div>
                  <div class="col-12">
                    <label class="form-label small">Notes</label>
                    <textarea class="form-control form-control-sm" rows="2" data-f="notes">${esc(p.notes)}</textarea>
                  </div>
                </div>
              </details>
            </div>
          </div>

          <div class="d-flex justify-content-end mt-2">
            <span class="small text-secondary">Line total: <strong data-line-total>${money.format(lineNet(p))}</strong></span>
          </div>
          <div class="invalid-feedback d-block small" data-errors></div>
        `;

        return wrap;
    }

    function esc(v) {
        return String(v ?? "").replace(/[&<>"']/g, c =>
            ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    }

    const LIST_FIELDS = ["deliverables", "acceptanceCriteria", "customerResponsibilities", "assumptions", "exclusions"];
    const NUMBER_FIELDS = ["quantity", "unitPrice", "vatRate", "discountValue"];

    listEl.addEventListener("input", onFieldChange);
    listEl.addEventListener("change", onFieldChange);

    function onFieldChange(event) {
        const field = event.target.dataset.f;
        if (!field) return;

        const wrap = event.target.closest("[data-client-id]");
        const p = positions.find(x => x.clientId === wrap.dataset.clientId);
        if (!p) return;

        if (event.target.type === "checkbox") {
            p[field] = event.target.checked;
            if (field === "isFree") render();
        } else if (LIST_FIELDS.includes(field)) {
            p[field] = event.target.value.split("\n").map(s => s.trim()).filter(Boolean);
        } else if (NUMBER_FIELDS.includes(field)) {
            p[field] = parseNumber(event.target.value);
        } else if (field === "durationPeriods") {
            const n = parseNumber(event.target.value);
            p[field] = n === null ? null : Math.round(n);
        } else {
            p[field] = event.target.value === "" ? null : event.target.value;
        }

        dirty = true;
        const totalEl = wrap.querySelector("[data-line-total]");
        if (totalEl) totalEl.textContent = money.format(lineNet(p));
        updateTotals();
    }

    listEl.addEventListener("click", function (event) {
        const op = event.target.closest("[data-op]")?.dataset.op;
        if (!op) return;

        const wrap = event.target.closest("[data-client-id]");
        const index = positions.findIndex(x => x.clientId === wrap.dataset.clientId);
        if (index < 0) return;

        if (op === "delete") {
            if (!window.confirm("Remove this position?")) return;
            positions.splice(index, 1);
        } else if (op === "duplicate") {
            const copy = JSON.parse(JSON.stringify(positions[index]));
            copy.clientId = cryptoId();
            copy.contractItemId = null;          // a copy is a new row
            copy.title = copy.title + " (copy)";
            positions.splice(index + 1, 0, copy);
        } else if (op === "up" && index > 0) {
            [positions[index - 1], positions[index]] = [positions[index], positions[index - 1]];
        } else if (op === "down" && index < positions.length - 1) {
            [positions[index + 1], positions[index]] = [positions[index], positions[index + 1]];
        }

        dirty = true;
        render();
    });

    // ---------------------------------------------------------------- totals

    function updateTotals() {
        const subtotal = positions.reduce((sum, p) => sum + lineNet(p), 0);
        const vat = positions.reduce((sum, p) => sum + lineNet(p) * ((Number(p.vatRate) || 0) / 100), 0);

        const gross = positions.reduce((sum, p) => {
            if (p.isFree) return sum;
            const unit = Number(p.unitPrice) || 0;
            const qty = Number(p.quantity) > 0 ? Number(p.quantity) : 0;
            return sum + (p.pricingModel === "Fixed" ? unit : unit * qty);
        }, 0);

        set("count", positions.length);
        set("subtotal", money.format(subtotal));
        set("discount", money.format(Math.max(0, gross - subtotal)));
        set("vat", money.format(vat));
        set("total", money.format(subtotal + vat));
    }

    function set(name, value) {
        const el = document.querySelector(`[data-total="${name}"]`);
        if (el) el.textContent = value;
    }

    // ---------------------------------------------------------------- server

    function token() {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : "";
    }

    async function post(handler, body) {
        const response = await fetch(`?handler=${handler}`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": token()
            },
            body: JSON.stringify(body)
        });

        try { return await response.json(); }
        catch { return { ok: false, message: "The server returned an unreadable response." }; }
    }

    function toast(type, message) {
        if (window.showToast) window.showToast(type, message);
        else window.alert(message);
    }

    // Guards against a double click producing two saves.
    async function guard(button, work) {
        if (busy) return;
        busy = true;
        const label = button ? button.innerHTML : null;
        if (button) { button.disabled = true; button.innerHTML = "Working…"; }
        try { await work(); }
        finally {
            busy = false;
            if (button) { button.disabled = false; button.innerHTML = label; }
        }
    }

    document.addEventListener("click", async function (event) {
        const action = event.target.closest("[data-action]")?.dataset.action;
        if (!action) return;
        const button = event.target.closest("[data-action]");

        if (action === "add-manual") {
            positions.push(normalise({ clientId: cryptoId(), sourceType: "Manual", catalogServiceId: null }));
            dirty = true;
            render();
            listEl.lastElementChild?.querySelector("[data-f='title']")?.focus();
        }

        if (action === "add-catalog") {
            if (!catalog.length) { toast("info", "The service catalog is empty."); return; }
            const name = window.prompt("Service name:\n" + catalog.map(c => "• " + c.name).join("\n"));
            if (!name) return;
            const match = catalog.find(c => c.name.toLowerCase() === name.trim().toLowerCase());
            if (!match) { toast("error", "No service with that name."); return; }

            positions.push(normalise({
                clientId: cryptoId(),
                sourceType: "Catalog",
                catalogServiceId: match.id,
                title: match.name,
                description: match.description || "",
                unit: match.unit || "",
                unitPrice: match.basePrice,
                pricingModel: "Fixed"
            }));
            dirty = true;
            render();
        }

        if (action === "save") {
            await guard(button, async () => {
                clearErrors();
                const result = await post("Save", positions);

                if (!result.ok) {
                    if (result.errors) showErrors(result.errors);
                    toast("error", result.message || "Could not save.");
                    return;
                }

                dirty = false;
                applyServerTotals(result.totals);
                toast("success", result.message || "Saved.");
            });
        }

        if (action === "organize") {
            organized = null;
            document.getElementById("organizeInputStep").classList.remove("d-none");
            document.getElementById("organizeReviewStep").classList.add("d-none");
            document.querySelector("[data-action='run-organize']").classList.remove("d-none");
            document.querySelector("[data-action='apply-organize']").classList.add("d-none");
            new bootstrap.Modal(document.getElementById("organizeModal")).show();
        }

        if (action === "run-organize") {
            await guard(button, async () => {
                const result = await post("Organize", {
                    roughInput: document.getElementById("roughInput").value,
                    currency: defaultCurrency,
                    positions: positions
                });

                if (!result.ok) {
                    // The user's positions are untouched.
                    toast("error", result.message || "The assistant could not help.");
                    return;
                }

                organized = result.positions.map(normalise);
                showReview(result);
            });
        }

        if (action === "apply-organize") {
            if (!organized) return;
            positions = organized;
            organized = null;
            dirty = true;
            render();
            bootstrap.Modal.getInstance(document.getElementById("organizeModal"))?.hide();
            toast("success", "Positions updated. Review them, then save.");
        }

        if (action === "generate-draft") {
            await guard(button, async () => {
                if (dirty) { toast("info", "Save your positions first."); return; }

                let result = await post("GenerateDraft", { overwriteApproved: false });

                if (!result.ok && result.needsConfirmation) {
                    if (!window.confirm(result.message + "\n\nReplace the approved wording?")) return;
                    result = await post("GenerateDraft", { overwriteApproved: true });
                }

                if (!result.ok) { toast("error", result.message || "Could not generate a draft."); return; }

                toast("success", result.message);
                window.location.reload();
            });
        }

        if (action === "approve-version") {
            const version = Number(button.dataset.version);
            if (!window.confirm(`Approve version ${version}? This becomes the contract wording.`)) return;

            await guard(button, async () => {
                const result = await post("ApproveDraft", { version });
                if (!result.ok) { toast("error", result.message); return; }
                toast("success", result.message);
                window.location.reload();
            });
        }
    });

    function applyServerTotals(totals) {
        if (!totals) return;
        const fmt = new Intl.NumberFormat("de-DE", { style: "currency", currency: totals.currency || defaultCurrency });
        set("count", totals.positionCount);
        set("subtotal", fmt.format(totals.subtotal));
        set("discount", fmt.format(totals.discount));
        set("vat", fmt.format(totals.vat));
        set("total", fmt.format(totals.total));
    }

    function clearErrors() {
        listEl.querySelectorAll("[data-errors]").forEach(el => { el.textContent = ""; });
        listEl.querySelectorAll(".border-danger").forEach(el => el.classList.remove("border-danger"));
    }

    function showErrors(errors) {
        errors.forEach(err => {
            const wrap = listEl.querySelector(`[data-client-id="${err.clientId}"]`);
            if (!wrap) return;
            wrap.classList.add("border-danger");
            const box = wrap.querySelector("[data-errors]");
            if (box) box.textContent = err.messages.join(" ");
        });
        listEl.querySelector(".border-danger")?.scrollIntoView({ behavior: "smooth", block: "center" });
    }

    function showReview(result) {
        document.getElementById("organizeInputStep").classList.add("d-none");
        document.getElementById("organizeReviewStep").classList.remove("d-none");
        document.querySelector("[data-action='run-organize']").classList.add("d-none");
        document.querySelector("[data-action='apply-organize']").classList.remove("d-none");

        const rejectedBox = document.getElementById("rejectedBox");
        const rejectedList = document.getElementById("rejectedList");
        rejectedList.innerHTML = "";

        if (result.rejected && result.rejected.length) {
            rejectedBox.classList.remove("d-none");
            result.rejected.forEach(r => {
                const li = document.createElement("li");
                li.textContent = `${r.positionTitle} — ${r.field}: kept ${r.before ?? "—"}, assistant suggested ${r.after ?? "—"}`;
                rejectedList.appendChild(li);
            });
        } else {
            rejectedBox.classList.add("d-none");
        }

        const changesList = document.getElementById("changesList");
        changesList.innerHTML = "";

        if (!result.changes || !result.changes.length) {
            changesList.innerHTML = "<li class='text-secondary'>No wording changes proposed.</li>";
            return;
        }

        result.changes.forEach(c => {
            const li = document.createElement("li");
            li.innerHTML = c.kind === "AddedPosition"
                ? `<strong>New position proposed:</strong> ${esc(c.after)} <span class="text-secondary">(no price set — you must price it)</span>`
                : `<strong>${esc(c.positionTitle)}</strong> — ${esc(c.field)} updated`;
            changesList.appendChild(li);
        });
    }

    window.addEventListener("beforeunload", function (event) {
        if (!dirty) return;
        event.preventDefault();
        event.returnValue = "";
    });

    render();
})();
