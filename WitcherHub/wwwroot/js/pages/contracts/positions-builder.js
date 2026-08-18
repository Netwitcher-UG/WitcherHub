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
    const suppliedDraftId = app.dataset.suppliedDraftId || null;
    const hasSuppliedText = app.dataset.hasText === "true";
    const emptyMessage = app.dataset.emptyMessage || "";

    let positions = (readJson("initialPositions") || []).map(normalise);
    let extraction = readJson("initialExtraction");
    let organized = null;
    let dirty = false;
    let busy = false;

    // Held across retries of one preparation attempt, so a repeat is recognised
    // as the same request rather than treated as a second one.
    let preparationKey = null;

    // The contract-level figures, for a contract whose money lives on the
    // contract rather than on positions.
    let contractMoney = readJson("contractMoney");

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
            sourceDraftId: null,
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

    // ------------------------------------------------------------------
    // One position, shown at the depth it actually needs.
    //
    // Every position used to render around twenty fields at once — VAT,
    // discount type and value, activation method, two dates, periods, scope,
    // deliverables — whatever the position was. A fixed one-off fee was asked
    // for a billing period and a duration; an hourly rate was given the same
    // prominence as a delivery date it would never have.
    //
    // What is shown now follows the pricing model and the billing cycle. The
    // essentials are always there; everything else is one click away and
    // labelled with what is inside, so nothing is hidden, only quiet.
    // ------------------------------------------------------------------

    /// Which fields this pricing model actually has an answer for.
    function shape(p) {
        const model = p.pricingModel || "Fixed";
        const recurring = p.billingCycle && p.billingCycle !== "OneTime";

        return {
            // A fixed amount is not a rate times a count, so asking for a
            // quantity invites a number that means nothing.
            quantity: model !== "Fixed",

            // The unit only means something once there is a quantity to count.
            unit: model !== "Fixed",

            // Duration belongs to a charge that repeats.
            periods: recurring,

            rateLabel:
                model === "Hourly" ? "Hourly rate"
                : model === "Unit" ? "Price per unit"
                : model === "Tiered" ? "Base rate"
                : "Amount",

            quantityLabel: model === "Hourly" ? "Hours" : "Quantity",

            hint:
                model === "Tiered"
                    ? "Tiered pricing is recorded as a base rate; describe the bands in the notes."
                : model === "Hourly"
                    ? "Leave the hours empty if no number of hours was agreed — the rate is still recorded."
                : ""
        };
    }

    function card(p, index) {
        const wrap = document.createElement("div");
        wrap.className = "border rounded-3 p-3";
        wrap.dataset.clientId = p.clientId;

        const s = shape(p);

        // A position read out of a supplied contract is neither manual nor from
        // the catalog, and labelling it "Catalog" would claim it came from a
        // saved service that does not exist.
        const badge =
            p.sourceType === "Manual"
                ? '<span class="badge bg-primary-subtle text-primary-emphasis">Manual</span>'
            : p.sourceType === "ExtractedFromContractText"
                ? '<span class="badge bg-info-subtle text-info-emphasis">From contract text</span>'
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

          <!-- ---- what every position needs, whatever it is ---- -->
          <div class="row g-2">
            <div class="col-12">
              <label class="form-label small">What is being charged for *</label>
              <input class="form-control form-control-sm" data-f="title" value="${esc(p.title)}"
                     placeholder="Name of the service or item" />
            </div>

            <div class="col-6 col-md-3">
              <label class="form-label small">How it is priced</label>
              <select class="form-select form-select-sm" data-f="pricingModel">
                ${PRICING_MODELS.map(m => `<option value="${m}"${p.pricingModel === m ? " selected" : ""}>${m}</option>`).join("")}
              </select>
            </div>

            <div class="col-6 col-md-3">
              <label class="form-label small">How often</label>
              <select class="form-select form-select-sm" data-f="billingCycle">
                ${BILLING_CYCLES.map(c => `<option value="${c}"${p.billingCycle === c ? " selected" : ""}>${c}</option>`).join("")}
              </select>
            </div>

            ${s.quantity ? `
            <div class="col-6 col-md-2">
              <label class="form-label small">${s.quantityLabel}</label>
              <input class="form-control form-control-sm" data-f="quantity" inputmode="decimal" value="${p.quantity ?? ""}" />
            </div>` : ""}

            ${s.unit ? `
            <div class="col-6 col-md-2">
              <label class="form-label small">Unit</label>
              <input class="form-control form-control-sm" data-f="unit" value="${esc(p.unit)}" placeholder="hour, item…" />
            </div>` : ""}

            <div class="col-6 col-md-2">
              <label class="form-label small">${s.rateLabel}</label>
              <input class="form-control form-control-sm" data-f="unitPrice" inputmode="decimal"
                     value="${p.unitPrice ?? ""}" ${p.isFree ? "disabled" : ""} />
            </div>

            ${s.periods ? `
            <div class="col-6 col-md-2">
              <label class="form-label small">For how many periods</label>
              <input class="form-control form-control-sm" data-f="durationPeriods" inputmode="numeric"
                     value="${p.durationPeriods ?? ""}" placeholder="e.g. 12" />
            </div>` : ""}
          </div>

          ${s.hint ? `<p class="small text-secondary mt-2 mb-0">${s.hint}</p>` : ""}

          <!-- ---- everything else, grouped and closed ---- -->
          <div class="mt-3 d-flex flex-column gap-1">
            <details class="wh-position-section">
              <summary class="small">Description and scope</summary>
              <div class="row g-2 mt-1">
                <div class="col-12">
                  <label class="form-label small">Description</label>
                  <textarea class="form-control form-control-sm wh-textarea-sm" rows="2" data-f="description">${esc(p.description)}</textarea>
                </div>
                <div class="col-12">
                  <label class="form-label small">Scope of work</label>
                  <textarea class="form-control form-control-sm wh-textarea-sm" rows="2" data-f="scope">${esc(p.scope)}</textarea>
                </div>
                <div class="col-12">
                  <label class="form-label small">Deliverables <span class="text-secondary">(one per line)</span></label>
                  <textarea class="form-control form-control-sm wh-textarea-sm" rows="2" data-f="deliverables">${esc((p.deliverables || []).join("\n"))}</textarea>
                </div>
                <div class="col-12 col-md-6">
                  <label class="form-label small">Service type</label>
                  <input class="form-control form-control-sm" data-f="serviceType" value="${esc(p.serviceType)}" />
                </div>
              </div>
            </details>

            <details class="wh-position-section">
              <summary class="small">Tax, discount and free-of-charge</summary>
              <div class="row g-2 mt-1">
                <div class="col-6 col-md-3">
                  <label class="form-label small">VAT %</label>
                  <input class="form-control form-control-sm" data-f="vatRate" inputmode="decimal" value="${p.vatRate ?? ""}" />
                </div>
                <div class="col-6 col-md-3">
                  <label class="form-label small">Discount type</label>
                  <select class="form-select form-select-sm" data-f="discountType">
                    <option value="">None</option>
                    <option value="Percent"${p.discountType === "Percent" ? " selected" : ""}>Percent</option>
                    <option value="Amount"${p.discountType === "Amount" ? " selected" : ""}>Amount</option>
                  </select>
                </div>
                ${p.discountType ? `
                <div class="col-6 col-md-3">
                  <label class="form-label small">Discount value</label>
                  <input class="form-control form-control-sm" data-f="discountValue" inputmode="decimal" value="${p.discountValue ?? ""}" />
                </div>` : ""}
                <div class="col-6 col-md-3 d-flex align-items-end">
                  <div class="form-check">
                    <input class="form-check-input" type="checkbox" data-f="isFree" id="free-${p.clientId}" ${p.isFree ? "checked" : ""} />
                    <label class="form-check-label small" for="free-${p.clientId}">Supplied free of charge</label>
                  </div>
                </div>
              </div>
            </details>

            <details class="wh-position-section">
              <summary class="small">Dates and activation</summary>
              <div class="row g-2 mt-1">
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
              </div>
            </details>

            <details class="wh-position-section">
              <summary class="small">Acceptance, responsibilities, assumptions, exclusions</summary>
              <div class="row g-2 mt-1">
                <div class="col-12 col-md-6">
                  <label class="form-label small">Acceptance criteria</label>
                  <textarea class="form-control form-control-sm wh-textarea-sm" rows="2" data-f="acceptanceCriteria">${esc((p.acceptanceCriteria || []).join("\n"))}</textarea>
                </div>
                <div class="col-12 col-md-6">
                  <label class="form-label small">Customer responsibilities</label>
                  <textarea class="form-control form-control-sm wh-textarea-sm" rows="2" data-f="customerResponsibilities">${esc((p.customerResponsibilities || []).join("\n"))}</textarea>
                </div>
                <div class="col-12 col-md-6">
                  <label class="form-label small">Assumptions</label>
                  <textarea class="form-control form-control-sm wh-textarea-sm" rows="2" data-f="assumptions">${esc((p.assumptions || []).join("\n"))}</textarea>
                </div>
                <div class="col-12 col-md-6">
                  <label class="form-label small">Exclusions</label>
                  <textarea class="form-control form-control-sm wh-textarea-sm" rows="2" data-f="exclusions">${esc((p.exclusions || []).join("\n"))}</textarea>
                </div>
                <div class="col-12">
                  <label class="form-label small">Notes</label>
                  <textarea class="form-control form-control-sm wh-textarea-sm" rows="2" data-f="notes">${esc(p.notes)}</textarea>
                </div>
              </div>
            </details>
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
        } else if (field === "pricingModel" || field === "billingCycle" || field === "discountType") {
            // Which fields are relevant follows these, so the card is redrawn.
            p[field] = event.target.value === "" ? null : event.target.value;
            dirty = true;
            render();
            return;
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
        // With no positions there is nothing to add up, and printing 0,00 € for a
        // contract that names a price in its text is a claim the system has no
        // basis for. The contract-level figures answer instead.
        if (positions.length === 0) {
            set("count", 0);
            set("subtotal", contractMoney ? "—" : "0,00 €");
            set("discount", "—");
            set("vat", contractMoney && contractMoney.vatRatePercent !== null ? contractMoney.vatRatePercent + " %" : "—");

            applyContractMoney(contractMoney);
            return;
        }

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

    /// Every action on this page goes through here.
    ///
    /// This used to end in `catch { message: "The server returned an unreadable
    /// response." }`, which is where a whole class of problems went to die. A
    /// signed-out session, an expired page token, a server error and a dropped
    /// connection all produced that one sentence, with no status, no reference
    /// and no next step — and because the two AI actions are the ones you reach
    /// for after a long time on the page, an ordinary session timeout read as
    /// "contract generation is broken".
    ///
    /// The server no longer redirects these requests, so a session that has
    /// ended now arrives as readable JSON. This handles the rest: what the
    /// browser could not even send, and what arrived in the wrong shape.
    async function post(handler, body) {
        let response;

        try {
            response = await fetch(`?handler=${handler}`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": token()
                },
                body: JSON.stringify(body),

                // Never follow a redirect silently. Following one is exactly how
                // an HTML login page ended up being parsed as JSON; seeing it as
                // a redirect lets us say what it means instead.
                redirect: "manual"
            });
        }
        catch {
            return {
                ok: false,
                transient: true,
                message: "The request could not reach the server. Check your connection and try again — " +
                    "nothing on this screen has been lost."
            };
        }

        // An opaque redirect: the session ended and the server wanted to send us
        // to the sign-in page.
        if (response.type === "opaqueredirect" || response.status === 0) {
            return {
                ok: false,
                sessionExpired: true,
                transient: false,
                message: "Your session has ended, so this could not be completed. Sign in again and retry — " +
                    "nothing on this screen has been lost."
            };
        }

        const text = await response.text();

        let result = null;
        try { result = text ? JSON.parse(text) : null; }
        catch { /* handled below, with the status to explain it */ }

        if (result && typeof result === "object") {
            // ProblemDetails from the global handler: title/detail, no ok flag.
            if (result.ok === undefined && (result.title || result.detail)) {
                return {
                    ok: false,
                    transient: response.status >= 500,
                    message: `${result.detail || result.title} (HTTP ${response.status})`
                };
            }

            return result;
        }

        // Something arrived that is not JSON. Name the status, because that is
        // the one thing that distinguishes these from each other.
        if (response.status === 401 || response.status === 403) {
            return {
                ok: false,
                sessionExpired: true,
                transient: false,
                message: "Your session has ended, so this could not be completed. Sign in again and retry — " +
                    "nothing on this screen has been lost."
            };
        }

        if (response.status === 502 || response.status === 503 || response.status === 504) {
            return {
                ok: false,
                transient: true,
                message: `The server did not answer in time (HTTP ${response.status}). This usually means the ` +
                    "request took too long. Try again, or with a shorter document."
            };
        }

        return {
            ok: false,
            transient: response.status >= 500,
            message: `The server answered HTTP ${response.status} in a form this page could not read. ` +
                "Reload the page and try again; if it keeps happening, the server log for this moment has the detail."
        };
    }

    // Validation and status messages, shown in the page.
    //
    // This used to fall back to window.alert, and since window.showToast does not
    // exist, every message on this page was a native browser dialog — modal,
    // stamped with the hostname, and blocking. It also froze the button that
    // raised it on "Working…" for as long as the dialog stood, which is the
    // stuck state in the screenshots.
    function toast(type, message) {
        if (window.UI?.toast?.show) {
            window.UI.toast.show({
                type: type === "info" ? "info" : type,
                msg: message,
                timeout: type === "error" ? 8000 : 4000
            });
            return;
        }

        if (window.showToast) { window.showToast(type, message); return; }

        // Last resort: an inline banner, still not a dialog.
        banner(type, message);
    }

    /// Sticks a message next to the section it is about, so validation is read
    /// where the problem is rather than in a corner of the screen.
    function banner(type, message, anchorId) {
        const anchor = document.getElementById(anchorId || "positionsApp");
        if (!anchor) return;

        let box = document.getElementById("inlineNotice");
        if (!box) {
            box = document.createElement("div");
            box.id = "inlineNotice";
            box.className = "alert radius-8 text-sm";
            anchor.prepend(box);
        }

        box.className = "alert radius-8 text-sm alert-" +
            (type === "error" ? "danger" : type === "success" ? "success" : type === "warning" ? "warning" : "info");
        box.textContent = message;
        box.scrollIntoView({ behavior: "smooth", block: "center" });
    }

    // Guards against a double click producing two saves.
    //
    // The button always comes back. It used to stay on "Working…" whenever the
    // work awaited something that never returned on its own — a modal dialog, or
    // a request with no timeout — so the page looked permanently busy.
    async function guard(button, work) {
        if (busy) return;
        busy = true;

        const label = button ? button.innerHTML : null;
        if (button) { button.disabled = true; button.innerHTML = "Working…"; }

        try { await work(); }
        catch (error) {
            // An unhandled failure has to surface as a message, not as a button
            // that never comes back.
            toast("error", "Something failed before the server answered. Nothing was changed.");
            if (window.console) console.error(error);
        }
        finally {
            busy = false;
            if (button) { button.disabled = false; button.innerHTML = label; }
        }
    }

    /// Steps out of the busy state for the duration of a prompt, then steps back
    /// in. Waiting for a person is not work in progress.
    async function releaseWhile(button, work) {
        const label = button ? button.innerHTML : null;

        busy = false;
        if (button) { button.disabled = false; button.innerHTML = label; }

        try { return await work(); }
        finally {
            busy = true;
            if (button) { button.disabled = true; button.innerHTML = "Working…"; }
        }
    }

    /// Shows the contract-level figures the server confirmed.
    ///
    /// A supplied contract with no positions has no position totals, and showing
    /// 0,00 € for one that names a price says something false. Where no total is
    /// agreed it says so instead.
    function applyContractMoney(m) {
        const totalEl = document.querySelector("[data-total='total']");
        const noteEl = document.getElementById("contractTotalNote");

        if (!m || !totalEl) return;

        if (m.agreedTotalNet === null || m.agreedTotalNet === undefined) {
            if (positions.length === 0) {
                totalEl.textContent = "Not specified";
                if (noteEl) {
                    noteEl.textContent = m.priceDeliberatelyUnspecified
                        ? "Confirmed: this contract deliberately names no price."
                        : "No total is stated in the supplied contract.";
                    noteEl.classList.remove("d-none");
                }
            }
            return;
        }

        const fmt = new Intl.NumberFormat("de-DE", {
            style: "currency",
            currency: m.currency || defaultCurrency
        });

        totalEl.textContent = fmt.format(m.agreedTotalNet);

        if (noteEl) {
            noteEl.textContent = "Contract-level total from the supplied contract.";
            noteEl.classList.remove("d-none");
        }
    }

    // ------------------------------------------------------------------
    // Saved-service picker
    //
    // Shows what a person needs to choose with: the name, what kind of service it
    // is, and what it normally costs. The chosen service is copied into a
    // position and can then be edited freely — the catalog entry is not changed,
    // and a later change to it does not reach back into this contract.
    // ------------------------------------------------------------------

    function addCatalogPosition(service) {
        positions.push(normalise({
            clientId: cryptoId(),
            sourceType: "Catalog",
            catalogServiceId: service.id,
            title: service.name,
            description: service.description || "",
            unit: service.unit || "",
            unitPrice: service.basePrice,
            pricingModel: "Fixed"
        }));

        dirty = true;
        render();
    }

    function catalogRow(service) {
        const price = money.format(service.basePrice || 0);

        return `
          <button type="button"
                  class="list-group-item list-group-item-action d-flex flex-wrap align-items-center justify-content-between gap-2 px-16 py-12"
                  data-action="pick-service" data-service-id="${escapeAttr(service.id)}">
            <span class="text-start">
              <span class="fw-medium d-block">${escapeHtml(service.name)}</span>
              ${service.description
                ? `<span class="text-secondary-light text-sm">${escapeHtml(service.description)}</span>`
                : ""}
            </span>
            <span class="fw-semibold text-nowrap">${escapeHtml(price)}</span>
          </button>`;
    }

    function openCatalogPicker() {
        const listHost = document.getElementById("catalogPickerList");
        const search = document.getElementById("catalogSearch");
        if (!listHost) return;

        const paint = function (term) {
            const needle = (term || "").trim().toLowerCase();
            const shown = needle
                ? catalog.filter(c =>
                    (c.name || "").toLowerCase().includes(needle) ||
                    (c.description || "").toLowerCase().includes(needle))
                : catalog;

            listHost.innerHTML = shown.length
                ? shown.map(catalogRow).join("")
                : '<p class="text-secondary-light text-sm mb-0 p-16">No saved service matches that.</p>';
        };

        paint("");

        if (search && !search.dataset.bound) {
            search.dataset.bound = "1";
            search.addEventListener("input", () => paint(search.value));
        }
        if (search) search.value = "";

        const modalEl = document.getElementById("catalogModal");
        if (modalEl && window.bootstrap?.Modal) {
            window.bootstrap.Modal.getOrCreateInstance(modalEl).show();
        }
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;").replaceAll("'", "&#039;");
    }

    function escapeAttr(value) { return escapeHtml(value); }

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
            // Was a window.prompt listing every service name and asking the user to
            // type one back exactly. A list you can search and click is the same
            // feature without the memory test.
            if (!catalog.length) { toast("info", "No saved services yet. Add a manual position instead."); return; }
            openCatalogPicker();
        }

        if (action === "pick-service") {
            const id = button.dataset.serviceId;
            const match = catalog.find(c => String(c.id) === String(id));
            if (!match) return;

            addCatalogPosition(match);
            toast("success", match.name + " added.");
        }

        if (action === "toggle-paste" || action === "focus-paste") {
            const panel = document.getElementById("pastePanel");
            if (!panel) return;

            const opening = action === "focus-paste" || panel.classList.contains("d-none");
            panel.classList.toggle("d-none", !opening);

            if (opening) {
                document.getElementById("contractTextSection")
                    ?.scrollIntoView({ behavior: "smooth", block: "start" });
                setTimeout(() => document.getElementById("pastedText")?.focus(), 250);
            }
        }

        if (action === "import-text") {
            const area = document.getElementById("pastedText");
            const text = (area?.value || "").trim();

            if (!text) { toast("error", "Paste the contract text first."); return; }

            await guard(button, async () => {
                const result = await post("ImportText", { text: text });

                if (!result.ok) { toast("error", result.message || "Could not store the text."); return; }

                toast("success", result.message || "Contract text stored.");
                window.location.reload();
            });
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
            // Unsaved positions are saved first rather than refused.
            //
            // This used to stop and say "Save your positions first". With no
            // positions there was nothing to save, and saving zero positions was
            // itself refused, so a contract built from pasted text sat in a loop
            // it could not leave. The save is silent: the user pressed "prepare",
            // and reporting "Positions saved" for it described neither what they
            // asked for nor what the action does.
            if (dirty) {
                const saved = await post("Save", positions);

                if (!saved.ok) {
                    if (saved.errors) showErrors(saved.errors);
                    toast("error", saved.message || "The positions could not be saved.");
                    return;
                }

                dirty = false;
                applyServerTotals(saved.totals);
            }

            // One key per attempt. A second click, or a retry after a timeout,
            // carries the same key and gets back the version the first request
            // made instead of adding another one.
            preparationKey = preparationKey || cryptoId();

            await guard(button, async () => {
                const result = await post("GenerateDraft", { idempotencyKey: preparationKey });

                if (!result.ok) {
                    // Preparation appends a version and replaces nothing, so it
                    // has nothing to confirm. Anything it refuses is a real
                    // failure, reported as one.
                    showFailure(result, "The contract could not be prepared.");
                    if (result.transient) offerRetry(button);
                    preparationKey = null;
                    return;
                }

                toast("success", result.message);

                // The new version is a draft awaiting review, so the user is taken
                // to it rather than left to find it.
                window.location.hash = "version-" + result.version;
                window.location.reload();
            });
        }

        // ---- supplied contract text: analysis and review ----

        if (action === "analyze-source") {
            const version = Number(button.dataset.version);

            await guard(button, async () => {
                const result = await runAnalysis(version, button);

                if (!result.ok) {
                    // The document is untouched and still the contract's source;
                    // only the optional reading of it failed.
                    showFailure(result, "The contract could not be analysed.");
                    offerRetry(button);
                    return;
                }

                extraction = result.extraction;
                renderExtraction();
                toast("success", result.message);
            });
        }

        if (action === "confirm-extraction") {
            const version = Number(button.dataset.version);
            if (!extraction) { toast("info", "There is nothing to confirm yet."); return; }

            await guard(button, async () => {
                collectExtractionEdits();

                const result = await post("ConfirmExtraction", { version, extraction });

                if (!result.ok) {
                    // Every edit stays on screen. Losing a review because a save
                    // failed would mean doing the whole thing again.
                    toast("error", result.message || "The confirmed values could not be saved.");
                    return;
                }

                // Refreshed from what the server stored, not from what was sent —
                // the message is only true of committed state, and the screen now
                // shows the same thing a reload would.
                if (result.extraction) {
                    extraction = result.extraction;
                    renderExtraction();
                }

                contractMoney = result.money;
        applyContractMoney(contractMoney);

                toast(result.confirmedCount === 0 ? "info" : "success", result.message);
            });
        }

        if (action === "extract-positions") {
            const version = Number(button.dataset.version);

            await guard(button, async () => {
                const result = await runAnalysis(version, button);

                if (!result.ok) { showFailure(result, "The contract could not be analysed."); offerRetry(button); return; }

                extraction = result.extraction;
                renderExtraction();

                if (!extraction.positions || extraction.positions.length === 0) {
                    toast("info",
                        "The contract names no separate services, so no positions were suggested. " +
                        "It can still be generated from the text.");
                    return;
                }

                document.getElementById("extractedPositionsBlock")?.scrollIntoView({ behavior: "smooth" });
            });
        }

        if (action === "add-extracted-positions") {
            const rows = Array.from(document.querySelectorAll("[data-extracted-index]"));
            const chosen = rows.filter(r => r.querySelector("input[type=checkbox]")?.checked);

            if (chosen.length === 0) { toast("info", "Tick at least one position to add."); return; }

            // Everything ticked is added. A charge whose amount the document
            // never states is added with that figure left empty rather than
            // withheld: the title, description, rate, unit and frequency are all
            // real information from the contract, and the missing number is one
            // only the user can supply. The position validator still refuses to
            // save a line with no price, so nothing unpriced reaches the
            // contract silently.
            let needsBillingChoice = 0;
            let needsFigures = 0;

            chosen.forEach(row => {
                const source = extraction.positions[Number(row.dataset.extractedIndex)];

                // Figures arrive exactly as they were read. Nothing is rounded,
                // recalculated or filled in on the way across.
                //
                // Quantity is the exception worth spelling out: it used to default
                // to 1 when the document did not state one, which turned a rate into
                // a line total and put money on the contract nobody had agreed. Only
                // a single fixed amount is a quantity of one, and that is because
                // 1 x the amount is the amount.
                const quantity =
                    source.quantity ?? (source.unitPrice === null && source.lineTotal !== null ? 1 : null);

                // A frequency the application cannot express arrives as null rather
                // than as one-off. Defaulting it silently turned a monthly charge
                // into a single charge.
                const cycle = BILLING_CYCLES.includes(source.billingCycle) ? source.billingCycle : null;
                if (cycle === null) needsBillingChoice++;

                if (source.canBecomePosition === false) needsFigures++;

                positions.push(normalise({
                    clientId: cryptoId(),
                    sourceType: "ExtractedFromContractText",
                    catalogServiceId: null,
                    sourceDraftId: suppliedDraftId || null,
                    sourceTermKey: source.termKey || null,
                    title: source.title || "",
                    description: source.description || "",
                    quantity: quantity ?? 1,
                    unit: source.unit || "",
                    unitPrice: source.unitPrice ?? null,
                    currency: source.currency || defaultCurrency,
                    vatRate: source.vatRatePercent ?? null,
                    billingCycle: cycle ?? "OneTime",

                    // What the document could not settle, carried onto the
                    // position so it is still visible after the reading has
                    // scrolled away and after a reload.
                    notes: source.blockedReason || ""
                }));
            });

            dirty = true;
            render();

            toast("success",
                chosen.length + " position(s) added for review. Check them and save — nothing is stored yet.");

            if (needsFigures > 0) {
                toast("warning",
                    needsFigures + " of them has no agreed amount in the contract. Enter the price and " +
                    "quantity, or mark the position as free — it cannot be saved until you do.");
            }

            if (needsBillingChoice > 0) {
                toast("info",
                    needsBillingChoice + " position(s) are billed on a frequency this app does not have. " +
                    "They were added as one-off — set the billing cycle before saving.");
            }

            document.getElementById("positionList")?.scrollIntoView({ behavior: "smooth" });
        }

        if (action === "approve-version") {
            const version = Number(button.dataset.version);

            // Asked before the button goes busy. Awaiting a dialog inside the
            // busy state is what left the button reading "Working…" for as long
            // as the dialog stood open.
            const go = await confirmDialog(
                `Approve version ${version}? It becomes the contract wording.`, "Approve version");

            if (!go) return;

            await guard(button, async () => {
                let result = await post("ApproveDraft", { version, confirmReplacingApproved: false });

                if (!result.ok && result.needsConfirmation) {
                    // Only here does anything become inactive, and the message says
                    // what happens to the version being replaced.
                    const replace = await releaseWhile(button, () =>
                        confirmDialog(result.message, "Approve this version"));

                    if (!replace) return;

                    result = await post("ApproveDraft", { version, confirmReplacingApproved: true });
                }

                if (!result.ok) { toast("error", result.message || "Could not approve this version."); return; }

                toast("success", result.message);
                window.location.reload();
            });
        }
    });

    /// Starts a reading of the supplied contract and waits for it, without
    /// holding a request open while it runs.
    ///
    /// One long request was the problem: reading a real contract takes longer
    /// than the platform proxy will hold a connection, so the browser was shown
    /// "HTTP 502 ... the request took too long" while the model was still
    /// working — and the answer, when it came, arrived into a request nobody was
    /// listening to. The reading now happens on the server's own time and this
    /// asks how it is getting on.
    ///
    /// Returns the same shape the old single call did, so the two callers did
    /// not have to change how they read the answer.
    async function runAnalysis(version, button) {
        const started = await post("Analyze", { version });
        if (!started.ok) return started;

        const startedAt = Date.now();
        let waited = 0;

        while (waited < ANALYSIS_GIVE_UP_MS) {
            // Gentle at first, then slower: most readings finish in the first
            // half-minute, and a long one does not need to be asked every second.
            const gap = waited < 30000 ? 1500 : 4000;
            await sleep(gap);
            waited = Date.now() - startedAt;

            const progress = await post("AnalysisStatus", { version });

            // A failure to ask is not a failure of the reading — the reading is
            // still going. Only stop for an answer that says so.
            if (progress.sessionExpired) return progress;

            if (progress.ok && progress.running) {
                showProgress(button, progress.elapsedSeconds || Math.round(waited / 1000));
                continue;
            }

            if (progress.ok || progress.running === false) {
                clearProgress(button);
                return progress;
            }
        }

        clearProgress(button);

        return {
            ok: false,
            transient: true,
            message: "The contract is taking unusually long to read. It is still being worked on — " +
                "reload the page in a minute to see the result."
        };
    }

    /// How long to keep asking before telling the user to come back later. The
    /// reading itself is not cancelled; only the waiting stops.
    const ANALYSIS_GIVE_UP_MS = 5 * 60 * 1000;

    function sleep(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    /// Counts up on the button, so a reading that takes a minute looks like work
    /// in progress rather than like a page that has stopped responding.
    function showProgress(button, seconds) {
        if (!button) return;
        button.innerHTML = `Reading… ${seconds}s`;
    }

    function clearProgress(button) {
        // guard() restores the original label when the work finishes; this only
        // stops the counter from being the last thing written.
        if (button) button.innerHTML = "Working…";
    }

    /// Shows a failed call, with the reference the server logged it under so a
    /// screenshot is enough to find it.
    /// Reports a failure in the medium that matches how long it will last.
    ///
    /// Everything used to go to a toast, which clears itself after eight seconds.
    /// That suits a transient outage — try again in a moment — but not a
    /// configuration fault: a wrong API key is still wrong tomorrow, and a
    /// disappearing message left the contract empty with nothing on screen saying
    /// why. A lasting problem gets a notice that lasts.
    function showFailure(result, fallback) {
        const message = result.message || fallback;

        // The server's own messages already end with "Reference XXXXXXXX." —
        // appending it again produced "... Reference 9A72F3B9. (reference
        // 9A72F3B9)". Only add it when the message does not already carry it.
        const full = result.reference && !message.includes(result.reference)
            ? `${message} (reference ${result.reference})`
            : message;

        toast("error", full);

        if (!result.transient) banner("error", full);

        // A session that has ended is the one failure with an obvious next step,
        // and it is not "try again" — every retry fails the same way until the
        // user signs in. Offer that instead of leaving them pressing the button.
        if (result.sessionExpired) offerSignIn(result.signInUrl);
    }

    /// Offers the only action that helps once the session has ended.
    ///
    /// Sign-in opens in a new tab deliberately: this page still holds positions
    /// and contract text the user has been editing, and navigating away from it
    /// to sign in would throw that work away — which is the opposite of what the
    /// message promises.
    function offerSignIn(url) {
        if (document.querySelector("[data-sign-in-again]")) return;

        const anchor = document.getElementById("positionsApp");
        if (!anchor) return;

        const target = url || `/Auth/Login?returnUrl=${encodeURIComponent(location.pathname + location.search)}`;

        const link = document.createElement("a");
        link.href = target;
        link.target = "_blank";
        link.rel = "noopener";
        link.className = "btn btn-primary text-sm px-16 py-8 radius-8 d-inline-flex align-items-center gap-8";
        link.innerHTML = '<i class="ri-login-circle-line"></i> Sign in again';

        // #positionsApp is a Bootstrap grid row, so anything dropped straight
        // into it becomes a flex item and stretches the full width. The wrapper
        // takes that role and lets the button keep its own size.
        const holder = document.createElement("div");
        holder.dataset.signInAgain = "1";
        holder.className = "col-12 mb-16";
        holder.appendChild(link);

        // Placed next to the notice, never inside it: banner() rewrites that
        // box's text on every message and would delete the link with it.
        const notice = document.getElementById("inlineNotice");
        if (notice) notice.insertAdjacentElement("afterend", holder);
        else anchor.prepend(holder);
    }

    /// Puts a retry next to the action that failed, so a transient outage does
    /// not mean starting over.
    function offerRetry(button) {
        if (!button || button.dataset.retryAdded === "1") return;

        const retry = document.createElement("button");
        retry.type = "button";
        retry.className = "btn btn-outline-primary text-sm px-16 py-8 radius-8 ms-2";
        retry.innerHTML = '<i class="ri-refresh-line"></i> Try again';
        retry.dataset.action = button.dataset.action;
        retry.dataset.version = button.dataset.version || "";

        button.dataset.retryAdded = "1";
        button.insertAdjacentElement("afterend", retry);
    }

    /// A confirmation that is part of the page rather than a browser dialog.
    async function confirmDialog(message, title) {
        if (window.UI?.modal?.confirm) {
            return window.UI.modal.confirm({ title: title || "Confirm", message, okText: "Continue" });
        }
        return window.confirm(message);
    }

    // ------------------------------------------------------------------
    // Review of what analysis read out of a supplied contract
    //
    // Every value is shown with what it was read from and has to be ticked
    // before it counts. Nothing here is applied to the contract until the user
    // presses save, and the source document is never modified.
    // ------------------------------------------------------------------

    const EXTRACTION_FIELDS = [
        ["title", "Title"], ["contractType", "Contract type"], ["purpose", "Purpose"],
        ["language", "Language"],
        ["providerName", "Our company"], ["providerAddress", "Our address"],
        ["providerRepresentative", "Our representative"],
        ["customerName", "Customer"], ["customerAddress", "Customer address"],
        ["customerRepresentative", "Customer representative"],
        ["effectiveDate", "Effective date"], ["startDate", "Start date"], ["endDate", "End date"],
        ["duration", "Duration"], ["renewalRules", "Renewal"], ["terminationNotice", "Termination notice"],
        ["totalPrice", "Total price"], ["currency", "Currency"], ["vatRate", "VAT rate"],
        ["vatTreatment", "VAT treatment"], ["discounts", "Discounts"],
        ["billingCycle", "Billing cycle"], ["paymentSchedule", "Payment schedule"],
        ["paymentDueDates", "Payment due dates"], ["deposit", "Deposit"],
        ["recurringCharges", "Recurring charges"],
        ["customerResponsibilities", "Customer responsibilities"],
        ["providerResponsibilities", "Our responsibilities"],
        ["acceptanceCriteria", "Acceptance"], ["revisions", "Revisions"],
        ["assumptions", "Assumptions"], ["exclusions", "Exclusions"],
        ["warranty", "Warranty"], ["liability", "Liability"],
        ["confidentiality", "Confidentiality"], ["intellectualProperty", "Intellectual property"],
        ["signatureParties", "Signatories"], ["otherTerms", "Other terms"]
    ];

    function renderExtraction() {
        const panel = document.getElementById("extractionReview");
        if (!panel || !extraction) return;

        panel.classList.remove("d-none");

        const body = document.getElementById("extractionBody");
        body.innerHTML = "";

        EXTRACTION_FIELDS.forEach(([key, label]) => {
            const field = extraction[key];
            if (!field) return;

            const stated = field.value !== null && field.value !== undefined && field.value !== "";

            const row = document.createElement("tr");
            row.dataset.field = key;
            row.innerHTML = `
                <td class="fw-medium">${escapeHtml(label)}</td>
                <td>
                    <input type="text" class="form-control form-control-sm radius-8"
                           data-extract-value value="${escapeAttr(field.value ?? "")}"
                           placeholder="${stated ? "" : "not stated in the contract"}">
                </td>
                <td class="text-secondary-light">
                    <span class="d-block text-truncate" style="max-width: 22rem"
                          title="${escapeAttr(field.sourceText ?? "")}">${escapeHtml(field.sourceText ?? "—")}</span>
                    <span class="text-sm">${stated ? Math.round((field.confidence ?? 0) * 100) + "% sure" : ""}</span>
                </td>
                <td class="text-end">
                    <input type="checkbox" class="form-check-input" data-extract-confirm
                           ${field.confirmed ? "checked" : ""}>
                </td>`;

            body.appendChild(row);
        });

        // Warnings
        const warnBox = document.getElementById("extractionWarnings");
        const warnList = document.getElementById("extractionWarningList");
        warnList.innerHTML = "";

        if (extraction.warnings && extraction.warnings.length) {
            warnBox.classList.remove("d-none");
            extraction.warnings.forEach(w => {
                const li = document.createElement("li");
                li.textContent = w;
                warnList.appendChild(li);
            });
        } else {
            warnBox.classList.add("d-none");
        }

        renderExtractedPositions();
    }

    /// An amount in the same shape as every other figure in the application:
    /// German separators and the currency symbol. These cells used to print the
    /// raw number, so a line total read "500" here and "500,00 EUR" everywhere
    /// else on the same screen.
    function amount(value, currency) {
        if (value === null || value === undefined) return "—";

        return new Intl.NumberFormat("de-DE", {
            style: "currency",
            currency: currency || defaultCurrency
        }).format(value);
    }

    function renderExtractedPositions() {
        const block = document.getElementById("extractedPositionsBlock");
        const body = document.getElementById("extractedPositionsBody");
        if (!block || !body) return;

        body.innerHTML = "";

        if (!extraction.positions || extraction.positions.length === 0) {
            block.classList.add("d-none");
            return;
        }

        block.classList.remove("d-none");

        extraction.positions.forEach((p, index) => {
            const row = document.createElement("tr");
            row.dataset.extractedIndex = String(index);

            // Every read position can be ticked, including the ones whose amount
            // the document never states.
            //
            // These used to be shown with the checkbox disabled, on the grounds
            // that adding one would invent a quantity. The reasoning was right
            // about inventing values and wrong about what to do: a services
            // contract is largely made of monthly fees, hourly rates and
            // conditional costs, so on a real document *every* row came back
            // disabled and the feature did nothing at all. Refusing does not
            // spare the user the missing number — it just means retyping the
            // title, the description, the rate and the frequency by hand first.
            //
            // So the reading is offered in full, and what it could not determine
            // is said plainly on the row. The user supplies the missing figure,
            // which they are the only one who knows, and the position validator
            // still refuses to save a line with no price behind it.
            const needsAttention = p.canBecomePosition === false;

            const tick = `<input type="checkbox" class="form-check-input"
                                 ${needsAttention ? 'data-needs-attention="true"' : ""}
                                 title="${escapeAttr(needsAttention ? p.blockedReason ?? "" : "")}">`;

            // How firm the money is, next to the money. Without it a rate nobody
            // committed to reads exactly like an agreed price.
            const commitment = p.commitment && p.commitment !== "Committed"
                ? `<span class="badge bg-neutral-200 text-neutral-600 border border-neutral-400
                           px-8 py-2 radius-4 text-sm ms-1">${escapeHtml(p.commitment)}</span>`
                : "";

            // The frequency in the document's own words when this app has no
            // matching cycle, so the difference is visible before it is chosen.
            const cycle = p.billingCycle
                ? `<span class="d-block text-secondary-light text-sm">${escapeHtml(p.billingCycle)}</span>`
                : p.billingCyclePhrase
                    ? `<span class="d-block text-warning-main text-sm">${escapeHtml(p.billingCyclePhrase)} — choose a cycle</span>`
                    : "";

            // Stated as what the user has to supply, not as a refusal.
            const blocked = needsAttention
                ? `<span class="d-block text-warning-main text-sm">
                       ${escapeHtml(p.blockedReason ?? "")} You can still add it and fill the figure in.
                   </span>`
                : "";

            row.innerHTML = `
                <td>${tick}</td>
                <td>
                    <span class="fw-medium">${escapeHtml(p.title ?? "")}</span>${commitment}
                    <span class="d-block text-secondary-light text-sm">${escapeHtml(p.description ?? "")}</span>
                    ${cycle}
                    ${blocked}
                </td>
                <td class="text-end">${p.quantity ?? "—"} ${escapeHtml(p.unit ?? "")}</td>
                <td class="text-end">${amount(p.unitPrice, p.currency)}</td>
                <td class="text-end">${amount(p.lineTotal, p.currency)}</td>`;

            body.appendChild(row);
        });
    }

    /// Reads the review table back, so corrections a person typed are what gets
    /// saved rather than what the analyser proposed.
    function collectExtractionEdits() {
        document.querySelectorAll("#extractionBody tr[data-field]").forEach(row => {
            const key = row.dataset.field;
            if (!extraction[key]) return;

            const value = row.querySelector("[data-extract-value]")?.value ?? "";
            extraction[key].value = value.trim() === "" ? null : value;
            extraction[key].confirmed = row.querySelector("[data-extract-confirm]")?.checked === true;
        });
    }

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

    // A stored extraction is shown straight away, so a review left half-finished
    // is still there when the page is opened again.
    if (extraction) renderExtraction();
})();
