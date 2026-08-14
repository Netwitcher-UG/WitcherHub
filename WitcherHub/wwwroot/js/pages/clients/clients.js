(function () {
    'use strict';
    window.UI = window.UI || {};
    const UI = window.UI;
    const mapUpdateBasic = {
        "Customer.Name": "vc-basic-name",
        "Name": "vc-basic-name",

        "Customer.FirstName": "vc-basic-firstName",
        "FirstName": "vc-basic-firstName",

        "Customer.LastName": "vc-basic-lastName",
        "LastName": "vc-basic-lastName",

        "Customer.EmailAddresses[0].Email": "vc-basic-email",
        "EmailAddresses[0].Email": "vc-basic-email",

        "Customer.Phone": "vc-basic-phone",
        "Phone": "vc-basic-phone",

        "Customer.TaxId": "vc-basic-taxId",
        "TaxId": "vc-basic-taxId",

        "Customer.Notes": "vc-basic-notes",
        "Notes": "vc-basic-notes",

        "Customer.Type": "vc-basic-type",
        "Type": "vc-basic-type",
    };
    const mapAddLocation = {
        "Address.Label": "vc-add-loc-label",
        "Address.City": "vc-add-loc-city",
        "Address.PostalCode": "vc-add-loc-postal",
        "Address.CountryCode": "vc-add-loc-country-select",
        "Address.Country": "vc-add-loc-country-select",
        "Address.StreetRaw": "vc-add-loc-streetRaw",
        "Address.AddressLine2": "vc-add-loc-line2",
        "Address.FullNameOrCompany": "vc-add-loc-fullname"
    };

    const mapAddContact = {
        "Contact.Salutation": "vc-add-c-salutation",
        "Contact.FirstName": "vc-add-c-firstName",
        "Contact.LastName": "vc-add-c-lastName",
        "Contact.Position": "vc-add-c-position",
        "Contact.Email": "vc-add-c-email",
        "Contact.Phone": "vc-add-c-phone",
    };

    const requiredCompanyContactFields = [
        { property: 'FirstName', valueKey: 'firstName', message: 'First name is required.' },
        { property: 'LastName', valueKey: 'lastName', message: 'Last name is required.' },
        { property: 'Position', valueKey: 'position', message: 'Position is required.' },
        { property: 'Email', valueKey: 'email', message: 'Email is required.' },
        { property: 'Phone', valueKey: 'phone', message: 'Phone is required.' }
    ];

    function validateRequiredCompanyContact(contact, map, prefixToClear) {
        if (prefixToClear) clearErrors(prefixToClear);

        let isValid = true;
        requiredCompanyContactFields.forEach(field => {
            const value = String(contact?.[field.valueKey] ?? '').trim();
            if (value) return;

            isValid = false;
            const inputId = map[`Contact.${field.property}`];
            if (inputId) setFieldError(inputId, field.message);
        });

        return isValid;
    }

    function setRequiredMarker(input, required) {
        if (!input?.id) return;

        const label = Array.from(document.querySelectorAll('label[for]'))
            .find(x => x.htmlFor === input.id);
        if (!label) return;

        let marker = label.querySelector('.vc-required-marker');
        if (required && !marker) {
            marker = document.createElement('span');
            marker.className = 'text-danger vc-required-marker ms-1';
            marker.textContent = '*';
            label.appendChild(marker);
        }

        if (marker) marker.classList.toggle('d-none', !required);
    }

    function setRequiredStateForContainer(container, required) {
        if (!container) return;

        const selector = [
            'input:not([type="hidden"]):not([type="checkbox"]):not([type="radio"]):not([type="button"]):not([type="submit"])',
            'select',
            'textarea'
        ].join(',');

        container.querySelectorAll(selector).forEach(input => {
            if (required) {
                input.setAttribute('required', 'required');
                input.setAttribute('aria-required', 'true');
            } else {
                input.removeAttribute('required');
                input.removeAttribute('aria-required');
            }

            setRequiredMarker(input, required);
        });
    }

    function initAddCompanyContactRequiredFields() {
        [
            'vc-add-c-firstName',
            'vc-add-c-lastName',
            'vc-add-c-position',
            'vc-add-c-email',
            'vc-add-c-phone'
        ].forEach(id => {
            const input = document.getElementById(id);
            if (!input) return;

            input.setAttribute('required', 'required');
            input.setAttribute('aria-required', 'true');
            setRequiredMarker(input, true);
        });
    }

    function mapUpdateLocation(idx) {
        return {
            "Address.Label": `vc-loc-${idx}-label`,
            "Address.FullNameOrCompany": `vc-loc-${idx}-fullname`,
            "Address.CountryCode": `vc-loc-${idx}-country-select`,
            "Address.Country": `vc-loc-${idx}-country-select`,
            "Address.City": `vc-loc-${idx}-city`,
            "Address.PostalCode": `vc-loc-${idx}-postal`,
            "Address.StreetRaw": `vc-loc-${idx}-streetRaw`,
            "Address.AddressLine2": `vc-loc-${idx}-line2`,
        };
    }
    

   
    function mapUpdateContact(idx) {
        return {
            "Contact.Salutation": `vc-c-${idx}-salutation`,
            "Contact.FirstName": `vc-c-${idx}-firstName`,
            "Contact.LastName": `vc-c-${idx}-lastName`,
            "Contact.Position": `vc-c-${idx}-position`,
            "Contact.Email": `vc-c-${idx}-email`,
            "Contact.Phone": `vc-c-${idx}-phone`,
        };
    }
    async function initClientDetailsPage() {
        const idEl = document.getElementById('vcPageClientId');
        if (!idEl) return;

        const id = (idEl.value || '').trim();
        if (!id) {
            toastError('Missing client id.', 'Error');
            return;
        }

        try {
            renderClient({ id, type: '—', name: 'Loading...', addresses: [], contacts: [], projects: [] });
            const client = await fetchClientById(id);
            renderClient(client);
        } catch (err) {
            console.error('Load client page failed:', err);
            toastError('Client not found or failed to load.', 'Error');
            renderClient({ id, type: '—', name: 'Client not found', addresses: [], contacts: [], projects: [] });
        }
    }

    // ---------- Helpers ----------
    function esc(s) {
        return (s ?? '').toString()
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }
    function initTooltips(root = document) {
        if (!window.bootstrap || !bootstrap.Tooltip) return;

        // فعّل tooltips للعناصر الجديدة
        root.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(el => {
            bootstrap.Tooltip.getOrCreateInstance(el, { container: 'body' });
        });
    }

    function $(id) { return document.getElementById(id); }
    

    
    function initSearchClearButtons(root = document) {
        root.querySelectorAll('.order-search').forEach(function (form) {
            if (form.dataset.clearInit === '1') return;
            form.dataset.clearInit = '1';

            const input = form.querySelector('input[type="text"][name="q"]');
            const clearBtn = form.querySelector('[data-search-clear]');
            if (!input || !clearBtn) return;

            function syncClearButton() {
                clearBtn.classList.toggle('d-none', !input.value.trim());
            }

            clearBtn.addEventListener('click', function () {
                input.value = '';
                syncClearButton();

                const pageInput = form.querySelector('input[name="p"]');
                if (pageInput) pageInput.value = '1';

                if (typeof form.requestSubmit === 'function') {
                    form.requestSubmit();
                } else {
                    form.submit();
                }
            });

            input.addEventListener('input', syncClearButton);
            syncClearButton();
        });
    }
    

    const VC_COUNTRIES = readCountryOptions();

    function readCountryOptions() {
        const el = document.getElementById('countryOptionsJson');
        if (!el) return [{ code: 'DE', name: 'Germany' }];

        try {
            const parsed = JSON.parse(el.textContent || '[]');
            const items = (parsed || [])
                .map(x => ({
                    code: String(x.code ?? x.value ?? '').trim().toUpperCase(),
                    name: String(x.name ?? x.text ?? '').trim()
                }))
                .filter(x => x.code.length === 2 && x.name.length > 0);

            return items.length ? items : [{ code: 'DE', name: 'Germany' }];
        } catch {
            return [{ code: 'DE', name: 'Germany' }];
        }
    }

    function countryCodeByName(name) {
        const normalized = String(name ?? '').trim().toLowerCase();
        const match = VC_COUNTRIES.find(x => x.name.toLowerCase() === normalized);
        return match?.code ?? null;
    }

    function countryNameByCode(code) {
        const normalized = String(code ?? '').trim().toUpperCase();
        const match = VC_COUNTRIES.find(x => x.code === normalized);
        return match?.name ?? 'Germany';
    }

    function buildCountrySelectOptionsHtml(selectedCode = 'DE') {
        const code = String(selectedCode ?? 'DE').trim().toUpperCase() || 'DE';

        return VC_COUNTRIES.map(x => `
        <option value="${esc(x.code)}" ${x.code === code ? 'selected' : ''}>${esc(x.name)}</option>
    `).join('');
    }

    function buildCountryMenuHtml(inputId, selectId, search = '', selectedCode = 'DE') {
        const code = String(selectedCode ?? 'DE').trim().toUpperCase() || 'DE';
        const q = String(search ?? '').trim().toLowerCase();

        const filtered = !q
            ? VC_COUNTRIES
            : VC_COUNTRIES.filter(x =>
                x.name.toLowerCase().includes(q) ||
                x.code.toLowerCase().includes(q));

        if (!filtered.length) {
            return `<li><button type="button" class="dropdown-item disabled">No results found</button></li>`;
        }

        return filtered.map(x => `
        <li>
            <button type="button"
                    class="dropdown-item ${x.code === code ? 'active' : ''}"
                    data-country-option="true"
                    data-country-input-id="${esc(inputId)}"
                    data-country-select-id="${esc(selectId)}"
                    data-country-code="${esc(x.code)}"
                    data-country-name="${esc(x.name)}">
                ${esc(x.name)}
                <span class="text-muted ms-2">${esc(x.code)}</span>
            </button>
        </li>
    `).join('');
    }

    function setCountryComboValue(inputId, menuId, selectId, code) {
        const input = $(inputId);
        const menu = $(menuId);
        const select = $(selectId);
        if (!input || !menu || !select) return;

        const normalizedCode = String(code ?? 'DE').trim().toUpperCase() || 'DE';

        select.innerHTML = buildCountrySelectOptionsHtml(normalizedCode);

        if ([...select.options].some(o => o.value === normalizedCode)) {
            select.value = normalizedCode;
        } else {
            select.value = 'DE';
        }

        const selectedName = countryNameByCode(select.value);
        input.value = selectedName;
        menu.innerHTML = buildCountryMenuHtml(inputId, selectId, input.value, select.value);

        const hiddenCountryNameId = select.getAttribute('data-country-name-target');
        if (hiddenCountryNameId) {
            const hiddenCountryName = $(hiddenCountryNameId);
            if (hiddenCountryName) {
                hiddenCountryName.value = selectedName;
            }
        }
    }
    function initCountryCombo(inputId, menuId, selectId, selectedCode = 'DE') {
        const select = $(selectId);
        const input = $(inputId);
        const menu = $(menuId);
        if (!select || !input || !menu) return;

        setCountryComboValue(inputId, menuId, selectId, select.value || selectedCode || 'DE');

        input.removeAttribute('readonly');
        input.removeAttribute('disabled');

        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown' || e.key === 'Enter') {
                if (window.bootstrap) {
                    bootstrap.Dropdown.getOrCreateInstance(input).show();
                }
            }
        });

        input.addEventListener('focus', function () {
            if (window.bootstrap) {
                bootstrap.Dropdown.getOrCreateInstance(input).show();
            }
        });
    }

    function filterCountryCombo(inputId, menuId, selectId) {
        const input = $(inputId);
        const menu = $(menuId);
        const select = $(selectId);
        if (!input || !menu || !select) return;

        const currentCode = String(select.value || 'DE').trim().toUpperCase() || 'DE';
        menu.innerHTML = buildCountryMenuHtml(inputId, selectId, input.value, currentCode);

        if (window.bootstrap) {
            bootstrap.Dropdown.getOrCreateInstance(input).show();
        }

        const hiddenCountryNameId = select.getAttribute('data-country-name-target');
        if (hiddenCountryNameId) {
            const hiddenCountryName = $(hiddenCountryNameId);
            if (hiddenCountryName) {
                hiddenCountryName.value = input.value.trim();
            }
        }
    }

    function getSelectedCountry(selectId) {
        const select = $(selectId);
        if (!select || !select.options.length) {
            return { code: 'DE', name: 'Germany' };
        }

        return {
            code: select.value || 'DE',
            name: select.options[select.selectedIndex]?.text || 'Germany'
        };
    }
    function initCreateFormCountryCombo() {
        initCountryCombo('create-country-combo', 'create-country-menu', 'Address_CountryCode', 'DE');
    }
    function setText(id, value) { const el = $(id); if (el) el.textContent = value ?? '—'; }
    function setHtml(id, html) { const el = $(id); if (el) el.innerHTML = html ?? ''; }

    function toastSuccess(msg, title) { UI?.toast?.success ? UI.toast.success(msg, title) : alert((title ? title + ": " : "") + msg); }
    function toastInfo(msg, title) { UI?.toast?.info ? UI.toast.info(msg, title) : alert((title ? title + ": " : "") + msg); }
    function toastError(msg, title) { UI?.toast?.error ? UI.toast.error(msg, title) : alert((title ? title + ": " : "") + msg); }

    function normalizeEmail(email) {
        return String(email ?? '').trim().toLowerCase();
    }

    function findDuplicateEmail(items) {
        const seen = new Set();

        for (const item of items || []) {
            const email = normalizeEmail(item?.email);
            if (!email) continue;

            if (seen.has(email)) return email;
            seen.add(email);
        }

        return null;
    }

    function createDeleteModalHelper() {
        const modalEl = document.getElementById('DeleteClientConfirmModal');
        const titleEl = document.getElementById('DeleteClientConfirmModalLabel');
        const messageEl = document.getElementById('DeleteClientConfirmModalMessage');
        const confirmBtn = document.getElementById('DeleteClientConfirmModalSubmit');

        if (!modalEl || !titleEl || !messageEl || !confirmBtn || !window.bootstrap) {
            return null;
        }

        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        let onConfirm = null;

        confirmBtn.addEventListener('click', async function () {
            if (!onConfirm) return;

            const fn = onConfirm;
            onConfirm = null;
            modal.hide();

            await fn();
        });

        modalEl.addEventListener('hidden.bs.modal', function () {
            onConfirm = null;
            confirmBtn.classList.remove('d-none');
            confirmBtn.disabled = false;
        });

        return {
            open: function (title, message, confirmText, callback, options = {}) {
                titleEl.textContent = title || 'Confirm';
                messageEl.textContent = message || 'Are you sure?';
                confirmBtn.textContent = confirmText || 'Delete';
                confirmBtn.classList.toggle('d-none', options.hideConfirm === true);
                confirmBtn.disabled = options.disableConfirm === true;
                onConfirm = options.hideConfirm === true ? null : callback;
                modal.show();
            }
        };
    }

    const deleteModalHelper = createDeleteModalHelper();

    function typeBadgeHtml(type) {
        const isCompany = type === 'Company';
        const cls = isCompany ? "badge bg-success bg-opacity-10 text-success" : "badge bg-info bg-opacity-10 text-info";
        return `<span class="${cls}">${esc(type)}</span>`;
    }

    function closeAllCollapses() {
        const ids = ['vc-deleteClientCollapse', 'vc-addLocationCollapse', 'vc-addContactCollapse'];
        ids.forEach(id => {
            const el = $(id);
            if (!el) return;
            bootstrap.Collapse.getOrCreateInstance(el, { toggle: false }).hide();
        });
    }
    // ---------- Show Lexware toast after reload (sessionStorage) ----------
    (function showLexwareToastFromStorage() {
        try {
            const raw = sessionStorage.getItem('lx_toast');
            if (!raw) return;

            sessionStorage.removeItem('lx_toast');
            const t = JSON.parse(raw);

            if (t?.type === 'success') toastSuccess(t.message, t.title);
            else if (t?.type === 'info') toastInfo(t.message, t.title);
            else if (t?.type === 'error') toastError(t.message, t.title);
        } catch { /* ignore */ }
    })();



    // ---------- Modal Session State ----------
    let currentClientId = null;
    let currentClient = null;
    let editingLocationIndex = null;
    let editingContactIndex = null;
    let editingBasic = false;
    function escapeHtml(str) {
        return (str ?? "").toString()
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }
    // ---------- Render Projects ----------
    function renderProjects(projects) {
        const tbody = document.getElementById("vc-projects");
        const countEl = document.getElementById("vc-projectCount");

        const list = Array.isArray(projects) ? projects : [];
        if (countEl) countEl.textContent = list.length;

        if (!tbody) return;

        if (list.length === 0) {
            tbody.innerHTML = `<tr><td colspan="4" class="text-muted">No projects.</td></tr>`;
            return;
        }

        const normStatusText = (v) => {
            if (v === null || v === undefined) return "";
            if (typeof v === "number") {
                const map = {
                    0: "Draft",
                    1: "Active",
                    2: "Closed",
                    3: "Canceled"
                };
                return map[v] ?? String(v);
            }
            return String(v);
        };

        const statusBadge = (status) => {
            const txt = normStatusText(status);
            const s = txt.toLowerCase();

            if (s.includes("draft")) return `<span class="badge bg-warning bg-opacity-10 text-warning">Draft</span>`;
            if (s.includes("active")) return `<span class="badge bg-success bg-opacity-10 text-success">Active</span>`;
            if (s.includes("closed") || s.includes("done") || s.includes("complete")) return `<span class="badge bg-secondary bg-opacity-10 text-secondary">Closed</span>`;
            if (s.includes("cancel")) return `<span class="badge bg-danger bg-opacity-10 text-danger">Canceled</span>`;

            return `<span class="badge bg-light text-dark">${esc(txt || "-")}</span>`;
        };

        const fmtDateOnly = (d) => {
            if (!d) return "—";
            const s = String(d);
            return s.includes("T") ? s.split("T")[0] : s;
        };

        const fmtRange = (start, end) => {
            const a = fmtDateOnly(start);
            const b = fmtDateOnly(end);

            if (a === "—" && b === "—") return "—";
            if (a !== "—" && b !== "—") return (a === b) ? a : `${a} → ${b}`;
            return a !== "—" ? a : b;
        };

        tbody.innerHTML = list.map(p => {
            const id = p.id ?? p.Id ?? p.projectId ?? p.ProjectId ?? "";
            const title = p.title ?? p.Title ?? p.name ?? p.Name ?? "(No title)";
            const status = p.status ?? p.Status ?? p.projectStatus ?? p.ProjectStatus ?? "";
            const startDate = p.startDate ?? p.StartDate ?? p.start ?? p.Start ?? null;
            const endDate = p.endDate ?? p.EndDate ?? p.end ?? p.End ?? null;

            const datesText = fmtRange(startDate, endDate);
            const openUrl = id ? `/Projects?openProjectId=${encodeURIComponent(id)}` : "javascript:;";

            return `
            <tr class="vc-project-row" data-open="${openUrl}" style="cursor:pointer;">
                <td class="fw-semibold">${esc(title)}</td>
                <td>${statusBadge(status)}</td>
                <td class="text-muted">${esc(datesText)}</td>
                <td class="text-end">
                    ${id
                    ? `
                        <a class="btn vc-icon-btn text-primary"
                           href="${openUrl}"
                           title="Open project">
                            <i class="ri-eye-line"></i>
                        </a>`
                    : `<span class="text-muted">—</span>`}
                </td>
            </tr>
        `;
        }).join("");

        tbody.querySelectorAll("tr.vc-project-row").forEach(tr => {
            tr.addEventListener("click", (e) => {
                if (e.target.closest("a")) return;
                const url = tr.getAttribute("data-open");
                if (url && url !== "javascript:;") window.location.href = url;
            });
        });
    }


    // ---------- Render Locations (inline edit) ----------
    function renderAddresses(list) {
        const wrap = $('vc-addressList');
        const count = $('vc-addressCount');
        const totalCount = (list?.length ?? 0);

        if (count) count.textContent = totalCount;
        if (!wrap) return;

        if (!list || !list.length) {
            wrap.innerHTML = `<div class="text-muted">No locations.</div>`;
            return;
        }

        wrap.innerHTML = list.map((a, idx) => {
            const isEditing = (editingLocationIndex === idx);

            const fullNameOrCompany = a.fullNameOrCompany ?? a.FullNameOrCompany ?? currentClient?.name ?? '';
            const streetRaw = a.streetRaw ?? a.street ?? '';
            const addressLine2 = a.addressLine2 ?? a.AddressLine2 ?? '';
            const postalCode = a.postalCode ?? a.PostalCode ?? '';
            const city = a.city ?? a.City ?? '';
            const country = a.country ?? a.Country ?? '';
            const countryCode = a.countryCode ?? a.CountryCode ?? '';
            const label = a.label ?? a.Label ?? 'Location';

            const selectedCountryCode = (countryCode || countryCodeByName(country) || 'DE').toUpperCase();
            const displayCountry = country || countryNameByCode(selectedCountryCode);

            const line1 = streetRaw ?? '';
            const line2 = addressLine2 ? ` • ${esc(addressLine2)}` : '';
            const addressText = (line1 || addressLine2) ? `${esc(line1)}${line2}` : '—';

            const cityText = `${esc(postalCode ?? '')} ${esc(city ?? '')}${(city || displayCountry) ? ', ' : ''}${esc(displayCountry ?? '')}`.trim() || '—';

            const isDefault = !!(a.isDefault ?? a.IsDefault ?? a.Default);
            const defaultBadge = isDefault ? `<span class="badge bg-primary bg-opacity-10 text-primary ms-2">Default</span>` : '';
            const starIcon = isDefault ? 'ri-star-fill' : 'ri-star-line';

            const canDelete = !isDefault && totalCount > 1;
            const deleteTitle = totalCount <= 1
                ? 'You cannot delete the last location'
                : isDefault
                    ? 'Default location cannot be deleted. Choose another default location first.'
                    : 'Delete';

            const deleteBtnClass = canDelete ? 'text-danger' : 'text-muted';
            const deleteDisabledAttr = canDelete ? '' : 'disabled aria-disabled="true"';

            return `
            <div class="card rounded-4 border bg-transparent shadow-none mb-0">
                <div class="card-body py-3">
                    <div class="d-flex align-items-start justify-content-between gap-3">

                        <div class="flex-grow-1">

                            <div class="${isEditing ? 'd-none' : ''}">
                                <div class="fw-semibold">
                                    ${esc(label)}
                                    ${defaultBadge}
                                </div>
                                <div class="text-muted small">${esc(fullNameOrCompany || '—')}</div>
                                <div class="text-muted small">${addressText}</div>
                                <div class="text-muted small">${cityText}</div>
                            </div>

                            <div class="${isEditing ? '' : 'd-none'}">
                                <div class="row g-2">

                                    <div class="col-12 col-md-4">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-label">
                                            Label <span class="text-danger">*</span>
                                            <i class="ri-information-line text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="Example: Billing / Shipping / Home"></i>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-label"
                                               value="${esc(label ?? '')}"
                                               placeholder="Billing"
                                               required />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-label"></div>
                                    </div>

                                    <div class="col-12 col-md-8">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-fullname">
                                            Full name / Company <span class="text-danger">*</span>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-fullname"
                                               value="${esc(fullNameOrCompany ?? '')}"
                                               placeholder="Customer or company name"
                                               required />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-fullname"></div>
                                    </div>

                                    <div class="col-12 col-md-8">
    <label class="form-label small mb-1" for="vc-loc-${idx}-country-combo">
        Country <span class="text-danger">*</span>
        <i class="ri-information-line text-muted ms-1"
              style="font-size:16px"
              data-bs-toggle="tooltip"
              title="Search & select country"></i>
    </label>

    <div class="dropdown">
        <input type="text"
               class="form-control form-control-sm dropdown-toggle"
               id="vc-loc-${idx}-country-combo"
               data-country-combo="true"
               data-country-menu="vc-loc-${idx}-country-menu"
               data-country-select="vc-loc-${idx}-country-select"
               data-bs-toggle="dropdown"
               aria-expanded="false"
               autocomplete="off"
               value="${esc(countryNameByCode(selectedCountryCode))}"
               placeholder="Search & select country..."
               required />

        <ul class="dropdown-menu w-100"
            id="vc-loc-${idx}-country-menu"
            aria-labelledby="vc-loc-${idx}-country-combo"
            style="max-height:280px; overflow-y:auto;">
            ${buildCountryMenuHtml(`vc-loc-${idx}-country-combo`, `vc-loc-${idx}-country-select`, countryNameByCode(selectedCountryCode), selectedCountryCode)}
        </ul>
    </div>

    <select id="vc-loc-${idx}-country-select"
            class="form-select"
            tabindex="-1"
            aria-hidden="true"
            style="position:absolute !important; left:-10000px !important; top:auto !important; width:1px !important; height:1px !important; overflow:hidden !important; opacity:0 !important; pointer-events:none !important;">
        ${buildCountrySelectOptionsHtml(selectedCountryCode)}
    </select>

    <div class="text-danger small mt-1" id="err-vc-loc-${idx}-country-select"></div>
</div>

                                    <div class="col-12 col-md-8">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-streetRaw">
                                            Street and house number <span class="text-danger">*</span>
                                            <i class="ri-information-line text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="Street and number. Example: Hauptstr. 12"></i>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-streetRaw"
                                               value="${esc(streetRaw ?? '')}"
                                               placeholder="Hauptstr. 12"
                                               required />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-streetRaw"></div>
                                    </div>

                                    <div class="col-12 col-md-4">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-city">City <span class="text-danger">*</span></label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-city"
                                               value="${esc(city ?? '')}"
                                               placeholder="Berlin"
                                               required />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-city"></div>
                                    </div>

                                    <div class="col-12 col-md-4">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-postal">Postal Code <span class="text-danger">*</span></label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-postal"
                                               value="${esc(postalCode ?? '')}"
                                               placeholder="10115"
                                               required />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-postal"></div>
                                    </div>

                                    <div class="col-12 col-md-8">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-line2">
                                            Address Line 2 <span class="text-danger">*</span>
                                            <i class="ri-information-line text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="Supplement, floor, apartment, etc."></i>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-line2"
                                               value="${esc(addressLine2 ?? '')}"
                                               placeholder="Floor 2, Apt 5"
                                               required />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-line2"></div>
                                    </div>

                                </div>
                            </div>

                        </div>

                        <div class="d-flex align-items-start gap-3">

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-primary"
                                    title="Set default"
                                    data-vc-action="set-default-location"
                                    data-index="${idx}">
                                <i class="${starIcon}"></i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-info ${isEditing ? 'd-none' : ''}"
                                    title="Edit"
                                    data-vc-action="edit-location"
                                    data-index="${idx}">
                                <i class="ri-edit-line"></i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-success ${isEditing ? '' : 'd-none'}"
                                    title="Save"
                                    data-vc-action="save-location"
                                    data-index="${idx}">
                                <i class="ri-check-line"></i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-muted ${isEditing ? '' : 'd-none'}"
                                    title="Cancel"
                                    data-vc-action="cancel-location"
                                    data-index="${idx}">
                                <i class="ri-close-line"></i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent ${deleteBtnClass}"
                                    title="${esc(deleteTitle)}"
                                    data-vc-action="delete-location"
                                    data-index="${idx}"
                                    ${deleteDisabledAttr}>
                                <i class="ri-delete-bin-line"></i>
                            </button>

                        </div>

                    </div>
                </div>
            </div>
        `;
        }).join('');

        if (typeof initTooltips === 'function') initTooltips(wrap);
    }


    // ---------- Render Contacts (inline edit) ----------
    function renderContacts(list) {
        const wrap = $('vc-contactList');
        const count = $('vc-contactCount');
        if (count) count.textContent = (list?.length ?? 0);
        if (!wrap) return;

        if (!list || !list.length) {
            wrap.innerHTML = `<div class="text-muted">No contacts.</div>`;
            return;
        }

        wrap.innerHTML = list.map((c, idx) => {
            const isEditing = (editingContactIndex === idx);

            const isPrimary = !!(c.isPrimary ?? c.IsPrimary ?? c.Primary);
            const primaryBadge = isPrimary ? `<span class="badge bg-warning bg-opacity-10 text-warning ms-2">Primary</span>` : '';
            const starIcon = isPrimary ? 'ri-star-fill' : 'ri-star-line';

            const salutation = c.salutation ?? c.Salutation ?? '';
            const firstName = c.firstName ?? c.FirstName ?? '';
            const lastName = c.lastName ?? c.LastName ?? '';
            const name = c.name ?? c.Name ?? '';
            const position = c.position ?? c.Position ?? '';
            const email = c.email ?? c.Email ?? '';
            const phone = c.phone ?? c.Phone ?? '';

            const displayName =
                (firstName || lastName)
                    ? `${salutation ? salutation + ' ' : ''}${(firstName || '')} ${(lastName || '')}`.trim()
                    : (name || '—');

            return `
            <div class="card rounded-4 border bg-transparent shadow-none mb-0">
                <div class="card-body py-3">
                    <div class="d-flex align-items-start justify-content-between gap-3">

                        <div class="flex-grow-1">

                            <!-- VIEW -->
                            <div class="${isEditing ? 'd-none' : ''}">
                                <div class="fw-semibold">
                                    ${esc(displayName)}
                                    ${primaryBadge}
                                </div>
                                <div class="text-muted small">${esc(position ?? '')}</div>
                                <div class="text-muted small">
                                    <i class="ri-send-plane-line align-middle me-1" style="font-size:18px"></i>${esc(email || '—')}
                                </div>
                                <div class="text-muted small">
                                    <i class="ri-phone-line align-middle me-1" style="font-size:18px"></i>${esc(phone || '—')}
                                </div>
                            </div>

                            <!-- EDIT -->
                            <div class="${isEditing ? '' : 'd-none'}">
                                <div class="row g-2">

                                    <div class="col-12 col-md-3">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-salutation">
                                            Salutation <small class="text-muted">(optional)</small>
                                            <i class="ri-information-line text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="Example: Herr / Frau / Mr / Ms"></i>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-salutation"
                                               value="${esc(salutation ?? '')}"
                                               placeholder="Herr" />
                                        <div class="text-danger small mt-1" id="err-vc-c-${idx}-salutation"></div>
                                    </div>

                                    <div class="col-12 col-md-4">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-firstName">First Name <span class="text-danger vc-required-marker">*</span></label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-firstName"
                                               required
                                               aria-required="true"
                                               value="${esc(firstName ?? '')}"
                                               placeholder="John" />
                                        <div class="text-danger small mt-1" id="err-vc-c-${idx}-firstName"></div>
                                    </div>

                                    <div class="col-12 col-md-5">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-lastName">Last Name <span class="text-danger vc-required-marker">*</span></label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-lastName"
                                               required
                                               aria-required="true"
                                               value="${esc(lastName ?? '')}"
                                               placeholder="Doe" />
                                        <div class="text-danger small mt-1" id="err-vc-c-${idx}-lastName"></div>
                                    </div>

                                    <div class="col-12 col-md-6">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-position">
                                            Position <span class="text-danger vc-required-marker">*</span>
                                            <i class="ri-information-line text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="Job title inside the company"></i>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-position"
                                               required
                                               aria-required="true"
                                               value="${esc(position ?? '')}"
                                               placeholder="Manager" />
                                        <div class="text-danger small mt-1" id="err-vc-c-${idx}-position"></div>
                                    </div>

                                    <div class="col-12 col-md-6">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-email">Email <span class="text-danger vc-required-marker">*</span></label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-email"
                                               type="email"
                                               required
                                               aria-required="true"
                                               value="${esc(email ?? '')}"
                                               placeholder="name@domain.com" />
                                        <div class="text-danger small mt-1" id="err-vc-c-${idx}-email"></div>
                                    </div>

                                    <div class="col-12 col-md-6">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-phone">Phone <span class="text-danger vc-required-marker">*</span></label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-phone"
                                               required
                                               aria-required="true"
                                               value="${esc(phone ?? '')}"
                                               placeholder="+49 ..." />
                                        <div class="text-danger small mt-1" id="err-vc-c-${idx}-phone"></div>
                                    </div>

                                </div>
                            </div>

                        </div>

                        <!-- Actions -->
                        <div class="d-flex align-items-start gap-3">

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-primary"
                                    title="Set primary"
                                    data-vc-action="set-primary-contact"
                                    data-index="${idx}">
                                <i class="${starIcon}"></i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-info ${isEditing ? 'd-none' : ''}"
                                    title="Edit"
                                    data-vc-action="edit-contact"
                                    data-index="${idx}">
                                <i class="ri-edit-line"></i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-success ${isEditing ? '' : 'd-none'}"
                                    title="Save"
                                    data-vc-action="save-contact"
                                    data-index="${idx}">
                                <i class="ri-check-line"></i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-muted ${isEditing ? '' : 'd-none'}"
                                    title="Cancel"
                                    data-vc-action="cancel-contact"
                                    data-index="${idx}">
                                <i class="ri-close-line"></i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-danger"
                                    title="Delete"
                                    data-vc-action="delete-contact"
                                    data-index="${idx}">
                                <i class="ri-delete-bin-line"></i>
                            </button>

                        </div>

                    </div>
                </div>
            </div>
        `;
        }).join('');

        // safe tooltips init (if you have initTooltips in your file)
        if (typeof initTooltips === 'function') initTooltips(wrap);
    }

    // ---------- Basic mode toggle ----------
    function setBasicMode(isEdit) {
        editingBasic = !!isEdit;


        clearErrors('vc-basic');

        const view = $('vc-basicView');
        const edit = $('vc-basicEdit');
        if (view) view.classList.toggle('d-none', editingBasic);
        if (edit) edit.classList.toggle('d-none', !editingBasic);
        if (currentClient) {
            toggleBasicNameFields(currentClient.type);
        }
        if (editingBasic && currentClient) {
            renderEmailEditRows(currentClient.emailAddresses ?? []);
        }
    }



    function lexwareBadgeHtml(status) {
        if (!status) return '';
        const isNum = typeof status === 'number';
        const s = isNum
            ? (status === 0 ? 'Imported' : status === 1 ? 'Exported' : 'NotExported')
            : status;

        const cls =
            s === 'Exported' ? "badge bg-primary bg-opacity-10 text-primary" :
                s === 'Imported' ? "badge bg-secondary bg-opacity-10 text-secondary" :
                    "badge bg-warning bg-opacity-10 text-warning";

        return `<span class="${cls}">${esc(s)}</span>`;
    }


    // ---- row click => open client modal (overview) ----
    document.addEventListener('click', function (e) {
        if (e.target.closest('.vc-actions-wrap')) return;
        if (e.target.closest('button, a, input, textarea, select, label')) return;

        const tr = e.target.closest('tr');
        if (!tr) return;

        const idEl = tr.querySelector('[data-client-id]');
        const id = idEl?.getAttribute('data-client-id') || '';
        if (!id) return;

        window.location.href = `/Clients/Details/${encodeURIComponent(id)}`;
    });



    document.addEventListener('input', function (e) {
        const input = e.target.closest('[data-country-combo="true"]');
        if (!input) return;

        const menuId = input.getAttribute('data-country-menu');
        const selectId = input.getAttribute('data-country-select');
        if (!menuId || !selectId) return;

        filterCountryCombo(input.id, menuId, selectId);
    });

    document.addEventListener('focusin', function (e) {
        const input = e.target.closest('[data-country-combo="true"]');
        if (!input) return;

        const menuId = input.getAttribute('data-country-menu');
        const selectId = input.getAttribute('data-country-select');
        if (!menuId || !selectId) return;

        filterCountryCombo(input.id, menuId, selectId);
    });

    document.addEventListener('click', function (e) {
        const option = e.target.closest('[data-country-option="true"]');
        if (option) {
            e.preventDefault();
            e.stopPropagation();

            const inputId = option.getAttribute('data-country-input-id');
            const selectId = option.getAttribute('data-country-select-id');
            const code = option.getAttribute('data-country-code');
            if (!inputId || !selectId || !code) return;

            const input = $(inputId);
            const menuId = input?.getAttribute('data-country-menu');
            if (!menuId) return;

            setCountryComboValue(inputId, menuId, selectId, code);

            if (window.bootstrap && input) {
                const dd = bootstrap.Dropdown.getOrCreateInstance(input);
                dd.hide();
                input.blur();
            }

            return;
        }

        const input = e.target.closest('[data-country-combo="true"]');
        if (!input) return;

        const menuId = input.getAttribute('data-country-menu');
        const selectId = input.getAttribute('data-country-select');
        if (!menuId || !selectId) return;

        filterCountryCombo(input.id, menuId, selectId);
    });


    document.addEventListener('DOMContentLoaded', function () {
        initCountryCombo('vc-add-loc-country-combo', 'vc-add-loc-country-menu', 'vc-add-loc-country-select', 'DE');
        initCreateFormCountryCombo();
        initAddCompanyContactRequiredFields();
        markAddressInputsRequired(document);

        const formModal = document.getElementById('FormModal');
        if (formModal) {
            formModal.addEventListener('shown.bs.modal', function () {
                initCreateFormCountryCombo();
                markAddressInputsRequired(formModal);
            });
        }

        initClientDetailsPage();
    });
    


    // ---------- Render Client ----------
    function renderClient(client) {
        setText('vc-name', client?.name ?? '—');
        setText('vc-subtitle', client?.type === 'Company' ? 'Company account' : 'Individual account');
        setHtml('vc-typeBadge', typeBadgeHtml(client?.type ?? '—'));
        setText('vc-idText', client?.id ? `ID: ${client.id}` : '—');

        renderEmailsView(client?.emailAddresses ?? []);
        setText('vc-phone', client?.phone || '—');
        setText('vc-taxId', client?.taxId || '—');
        setText('vc-notes', client?.notes || '—');
        setText('vc-lx-customerNumber', client?.lexwareCustomerNumber ?? '—');
        setText('vc-lx-version', client?.lexwareVersion ?? '—');
        setText('vc-lx-contactId', client?.lexwareContactId ?? '—');
        setText('vc-lx-organizationId', client?.lexwareOrganizationId ?? '—');
        setText('vc-lx-archived', fmtBool(client?.lexwareArchived));
        setText('vc-lx-taxFree', fmtBool(client?.lexwareAllowTaxFreeInvoices));
        setText('vc-lx-syncedAt', fmtDate(client?.lexwareSyncedAtUtc));
        const fn = document.getElementById("vc-basic-firstName");
        if (fn) fn.value = client?.firstName ?? "";

        const ln = document.getElementById("vc-basic-lastName");
        if (ln) ln.value = client?.lastName ?? "";


        // Lexware badge + Export button
        setHtml('vc-lexwareBadge', lexwareBadgeHtml(client?.lexwareType));

        const exportBtn = $('vc-exportBtn');
        if (exportBtn) {
            const lex = client?.lexwareType;
            const isNotExported = (lex === 'NotExported') || (lex === 2);
            exportBtn.classList.toggle('d-none', !isNotExported);
            exportBtn.dataset.clientId = client?.id ?? '';
        }

        // Fill basic edit inputs (for your existing markup)
        const typeSel = $('vc-basic-type'); if (typeSel) typeSel.value = client?.type ?? 'Individual';
        const nameInp = $('vc-basic-name');
        if (nameInp) {
            nameInp.value = (client?.type === "Company") ? (client?.name ?? '') : '';
        }

        const phoneInp = $('vc-basic-phone'); if (phoneInp) phoneInp.value = client?.phone ?? '';
        const taxInp = $('vc-basic-taxId'); if (taxInp) taxInp.value = client?.taxId ?? '';
        const notesInp = $('vc-basic-notes'); if (notesInp) notesInp.value = client?.notes ?? '';

        // default: view mode
        setBasicMode(false);

        // addresses
        renderAddresses(client?.addresses ?? []);

        // contacts (Company only)
        const companySection = $('vc-companyContactSection');
        const isCompany = client?.type === 'Company';
        if (companySection) {
            if (!isCompany) {
                companySection.classList.add('d-none');
                renderContacts([]);
            } else {
                companySection.classList.remove('d-none');
                renderContacts(client?.contacts ?? []);
            }
        }

        // projects
        renderProjects(client?.projects ?? []);

        // hidden ids (server forms)
        const delClient = $('vc-clientId-deleteClient'); if (delClient) delClient.value = client?.id ?? '';
        const basicId = $('vc-clientId-basic'); if (basicId) basicId.value = client?.id ?? '';

        // hidden ids (add forms)
        const a1 = $('vc-clientId-addLocation'); if (a1) a1.value = client?.id ?? '';
        const a2 = $('vc-clientId-addContact'); if (a2) a2.value = client?.id ?? '';

        const addLocFullName = $('vc-add-loc-fullname');
        if (addLocFullName) addLocFullName.value = client?.name ?? '';

        initCountryCombo('vc-add-loc-country-combo', 'vc-add-loc-country-menu', 'vc-add-loc-country-select', 'DE');

        currentClientId = client?.id ?? null;
        currentClient = client ?? null;
        toggleBasicNameFields(client?.type ?? "Individual");
    }
    function renderEmailEditRows(list) {
        const wrap = $('vc-basic-email-list');
        if (!wrap) return;

        const emails = (list && list.length ? list : [{ id: null, kind: 'business', email: '' }])
            .map(x => ({
                id: x.id ?? x.Id ?? null,
                kind: x.kind ?? x.Kind ?? 'business',
                email: x.email ?? x.Email ?? ''
            }));

        wrap.innerHTML = emails.map((x, i) => `
        <div class="row g-2 align-items-end"
             data-email-row="basic"
             data-index="${i}"
             data-email-id="${x.id ?? ''}">

            <div class="col-12 col-md-4">
                <select class="form-select form-select-sm" id="vc-basic-email-kind-${i}">
                    <option value="business" ${String(x.kind).toLowerCase() === 'business' ? 'selected' : ''}>business</option>
                    <option value="private" ${String(x.kind).toLowerCase() === 'private' ? 'selected' : ''}>private</option>
                    <option value="other" ${String(x.kind).toLowerCase() === 'other' ? 'selected' : ''}>other</option>
                </select>
                <div class="text-danger small mt-1" id="err-vc-basic-email-kind-${i}"></div>
            </div>

            <div class="col-12 col-md-7">
                <input class="form-control form-control-sm"
                       id="vc-basic-email-email-${i}"
                       type="email"
                       value="${esc(x.email)}"
                       placeholder="email@domain.com" />
                <div class="text-danger small mt-1" id="err-vc-basic-email-email-${i}"></div>
            </div>

            <div class="col-12 col-md-1 d-flex justify-content-end">
                <button type="button"
                        class="btn p-0 border-0 bg-transparent text-danger vc-basic-email-remove"
                        title="Remove"
                        ${emails.length === 1 ? 'disabled' : ''}>
                    <i class="ri-delete-bin-line"></i>
                </button>
            </div>
        </div>
    `).join('');
    }


    function collectEmailEditRows() {
        const rows = Array.from(document.querySelectorAll('[data-email-row="basic"]'));
        const items = rows.map((row, idx) => {
            const i = Number(row.getAttribute('data-index') ?? idx);
            const kind = document.getElementById(`vc-basic-email-kind-${i}`)?.value ?? 'business';
            const email = (document.getElementById(`vc-basic-email-email-${i}`)?.value ?? '').trim();

            const rawId = row.getAttribute('data-email-id') || null;
            const id = rawId && rawId.length ? rawId : null;

            return { id, kind, email };
        }).filter(x => x.email.length > 0);

        return items;
    }


    function buildMapUpdateBasicDynamic(emailCount) {
        const map = { ...mapUpdateBasic };

        for (let i = 0; i < emailCount; i++) {
            map[`Customer.EmailAddresses[${i}].Email`] = `vc-basic-email-email-${i}`;
            map[`EmailAddresses[${i}].Email`] = `vc-basic-email-email-${i}`;

            map[`Customer.EmailAddresses[${i}].Kind`] = `vc-basic-email-kind-${i}`;
            map[`EmailAddresses[${i}].Kind`] = `vc-basic-email-kind-${i}`;
        }
        return map;
    }

    

    // ---------- Create Modal (FormModal) : show/hide contact section ----------
    document.addEventListener('DOMContentLoaded', function () {
        const modalEl = document.getElementById('FormModal');
        if (!modalEl) return;

        const form = modalEl.querySelector('form');
        const typeSelect = modalEl.querySelector('#type');
        const contactSection = modalEl.querySelector('#contactSection');
        const modalTitle = modalEl.querySelector('.modal-title');

        if (!form || !typeSelect || !contactSection) return;

        function updateCreateModalUI() {
            const isCompany = typeSelect.value === 'Company';

            contactSection.classList.toggle('d-none', !isCompany);
            setRequiredStateForContainer(contactSection, isCompany);

            if (modalTitle) {
                modalTitle.textContent = isCompany ? 'Add Company' : 'Add Individual';
            }

            modalEl.querySelectorAll(".individual-only").forEach(x => {
                x.classList.toggle("d-none", isCompany);
            });

            modalEl.querySelectorAll(".company-only").forEach(x => {
                x.classList.toggle("d-none", !isCompany);
            });

            const firstName = modalEl.querySelector('#firstName');
            const lastName = modalEl.querySelector('#lastName');
            const companyName = modalEl.querySelector('#companyName');

            // Only submit and validate the fields that belong to the selected type.
            // Company does not have Customer.FirstName/Customer.LastName.
            if (companyName) {
                companyName.disabled = !isCompany;
                companyName.toggleAttribute('required', isCompany);
                companyName.setAttribute('aria-required', isCompany ? 'true' : 'false');
            }

            if (firstName) {
                firstName.disabled = isCompany;
                firstName.toggleAttribute('required', !isCompany);
                firstName.setAttribute('aria-required', !isCompany ? 'true' : 'false');
            }

            if (lastName) {
                lastName.disabled = isCompany;
                lastName.toggleAttribute('required', !isCompany);
                lastName.setAttribute('aria-required', !isCompany ? 'true' : 'false');
            }
        }

        function clearRazorValidationState(form) {
            if (window.jQuery) {
                const $form = window.jQuery(form);
                const validator = $form.data('validator');
                const unobtrusive = $form.data('unobtrusiveValidation');

                if (validator && typeof validator.resetForm === 'function') {
                    validator.resetForm();
                }

                if (unobtrusive) {
                    $form.removeData('validator');
                    $form.removeData('unobtrusiveValidation');
                    window.jQuery.validator.unobtrusive.parse(form);
                }
            }

            form.querySelectorAll('[data-valmsg-for]').forEach(el => {
                el.textContent = '';
                el.classList.remove('field-validation-error');
                el.classList.add('field-validation-valid');
            });

            form.querySelectorAll('.input-validation-error').forEach(el => {
                el.classList.remove('input-validation-error');
                el.removeAttribute('aria-invalid');
            });

            form.querySelectorAll('.validation-summary-errors, .validation-summary-valid, [data-valmsg-summary="true"]').forEach(el => {
                el.classList.remove('validation-summary-errors');
                el.classList.add('validation-summary-valid');
                el.innerHTML = '';
            });

            form.querySelectorAll('.text-danger').forEach(el => {
                if (el.hasAttribute('data-valmsg-for') || el.hasAttribute('data-valmsg-summary')) {
                    el.textContent = '';
                }
            });
        }

        function resetCreateCustomerModal() {
            // 1) امسح الحقول النصية
            modalEl.querySelectorAll('input[type="text"], input[type="email"], input[type="tel"], textarea').forEach(el => {
                el.value = '';
            });

            // 2) reset للـ checkbox / radio
            modalEl.querySelectorAll('input[type="checkbox"], input[type="radio"]').forEach(el => {
                el.checked = el.defaultChecked;
            });

            // 3) reset للـ selects
            modalEl.querySelectorAll('select').forEach(sel => {
                if (sel.id === 'type') {
                    sel.value = 'Individual';
                } else if (sel.id === 'Address_CountryCode') {
                    sel.value = 'DE';
                } else {
                    sel.selectedIndex = 0;
                }
            });

            // 4) hidden country name
            const hiddenCountry = modalEl.querySelector('#Address_Country');
            if (hiddenCountry) hiddenCountry.value = 'Germany';

            // 5) reset قائمة الإيميلات إلى صف واحد فقط
            const emailList = modalEl.querySelector('#create-email-list');
            if (emailList) {
                emailList.innerHTML = '';
                emailList.appendChild(createEmailRowDom(0, 'business', ''));
                renumberCreateEmailRows(modalEl);
            }

            // 6) reset country combo
            initCreateFormCountryCombo();

            // 7) reset validation
            clearRazorValidationState(form);

            // 8) update UI حسب النوع
            updateCreateModalUI();

            // 9) reparse unobtrusive validation بعد إعادة بناء الإيميلات
            if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
                window.jQuery(form).removeData('validator');
                window.jQuery(form).removeData('unobtrusiveValidation');
                window.jQuery.validator.unobtrusive.parse(form);
            }
        }

        typeSelect.addEventListener('change', updateCreateModalUI);

        modalEl.addEventListener('shown.bs.modal', function () {
            updateCreateModalUI();
        });

        modalEl.addEventListener('hidden.bs.modal', function () {
            resetCreateCustomerModal();
        });

        form.addEventListener('submit', function (e) {
            const inputs = Array.from(
                modalEl.querySelectorAll(
                    '#create-email-list [data-email-row="create"] input[type="email"]'));

            const items = inputs.map(input => ({ email: input.value }));
            const duplicateEmail = findDuplicateEmail(items);
            if (!duplicateEmail) return;

            e.preventDefault();

            inputs.forEach(input => {
                if (normalizeEmail(input.value) !== duplicateEmail) return;

                input.classList.add('input-validation-error');
                input.setAttribute('aria-invalid', 'true');

                const message = modalEl.querySelector(
                    `[data-valmsg-for="${CSS.escape(input.name)}"]`);
                if (message) {
                    message.textContent = 'The same email cannot be entered more than once.';
                    message.classList.remove('field-validation-valid');
                    message.classList.add('field-validation-error');
                }
            });

            toastError('The same email cannot be entered more than once.', 'Validation');
        });

        updateCreateModalUI();
    });
    // ---------- Delegated actions (Basic + Locations + Contacts) ----------
    document.addEventListener('click', async function (e) {
        const b = e.target.closest('[data-vc-action]');
        if (!b) return;

        const action = b.getAttribute('data-vc-action');
        const idx = Number(b.getAttribute('data-index') ?? -1);

        if (!currentClient) return;       
        const client = currentClient;

        // ✅ ---- Basic actions FIRST ----
        if (action === 'edit-basic') { setBasicMode(true); return; }
        if (action === 'cancel-basic') { setBasicMode(false); return; }
        if (action === 'export-lexware') {
            if (client?.lexwareType !== 'NotExported') return;

            const url = document.getElementById('vcLexwareExportUrl')?.value;
            if (!url) return toastError('Export url not found', 'Lexware');

            try {
                b.disabled = true;
                toastInfo('Exporting to Lexware...', 'Lexware');

                await postJson(url, { customerId: client.id });

                sessionStorage.setItem('lx_toast', JSON.stringify({
                    type: 'success',
                    title: 'Lexware',
                    message: 'Exported successfully.'
                }));

                window.location.reload();
            } catch (err) {
                console.error(err);
                toastError(err?.payload?.message || 'Export failed.', 'Lexware');
            } finally {
                b.disabled = false;
            }
            return;
        }

        if (action === 'save-basic') {
            if (!currentClient) return;

            const url = document.getElementById('vcUpdateBasicUrl')?.value;
            if (!url) { toastError('Update url not found.', 'Error'); return; }

            const emailItems = collectEmailEditRows();
            const duplicateEmail = findDuplicateEmail(emailItems);

            if (duplicateEmail) {
                document
                    .querySelectorAll('[data-email-row="basic"] input[type="email"]')
                    .forEach(input => {
                        if (normalizeEmail(input.value) === duplicateEmail) {
                            setFieldError(
                                input.id,
                                'The same email cannot be entered more than once.');
                        }
                    });

                toastError('The same email cannot be entered more than once.', 'Validation');
                return;
            }

            if (emailItems.length === 0) {
                toastError('You must keep at least one email.', 'Validation');
                const g = $('err-vc-basic-email-global');
                if (g) { g.classList.remove('d-none'); g.textContent = 'At least one email address is required.'; }
                return;
            } else {
                const g = $('err-vc-basic-email-global');
                if (g) { g.classList.add('d-none'); g.textContent = ''; }
            }

            const globalErr = $('err-vc-basic-email-global');

            if (emailItems.length === 0) {
                if (globalErr) {
                    globalErr.classList.remove('d-none');
                    globalErr.textContent = 'You must keep at least one email.';
                }
                toastError('At least one email is required.', 'Validation');
                return;
            } else if (globalErr) {
                globalErr.classList.add('d-none');
                globalErr.textContent = '';
            }

            const selectedType = $('vc-basic-type')?.value ?? currentClient.type;

            const finalName = selectedType === "Company"
                ? ($('vc-basic-name')?.value?.trim() ?? currentClient.name)
                : `${$('vc-basic-firstName')?.value?.trim() ?? ''} ${$('vc-basic-lastName')?.value?.trim() ?? ''}`.trim();

            const payload = {
                customerId: currentClient.id,
                customer: {
                    type: selectedType,
                    firstName: $('vc-basic-firstName')?.value?.trim() ?? '',
                    lastName: $('vc-basic-lastName')?.value?.trim() ?? '',
                    name: finalName,

                    emailAddresses: emailItems,
                    phone: $('vc-basic-phone')?.value?.trim() ?? '',
                    taxId: $('vc-basic-taxId')?.value?.trim() ?? '',
                    notes: $('vc-basic-notes')?.value?.trim() ?? ''
                }
            };




            try {
                const updatedRaw = await postJson(url, payload);
                const updated = normalizeClient(updatedRaw) || currentClient;
                currentClient = updated;
                renderClient(currentClient);
                toastSuccess('Saved successfully.', 'Success');
            } catch (err) {
                console.error(err);
                if (err?.status === 400 && err?.payload?.errors) {
                    const emailCountForMap = Math.max(1, (document.querySelectorAll('[data-email-row="basic"]').length || 1));
                    showServerErrors(err.payload.errors, buildMapUpdateBasicDynamic(emailCountForMap), "vc-basic");

                    toastError('Please fix the highlighted fields.', 'Validation');
                    return;
                }

                toastError(err?.payload?.message || 'Failed to save.', 'Error');
            }
            return;
        }
        function showServerErrorsBasic(errors) {
            clearErrors('vc-basic');
            const g = $('err-vc-basic-email-global');
            if (g) { g.classList.add('d-none'); g.textContent = ''; }

            (errors || []).forEach(e => {
                const f = (e.field || '').toString();

                // EmailAddresses[i].Email or Kind
                const m = f.match(/EmailAddresses\[(\d+)\]\.(Email|Kind)/i);
                if (m) {
                    const idx = Number(m[1]);
                    const prop = (m[2] || '').toLowerCase();
                    const id = prop === 'email' ? `vc-basic-email-${idx}` : `vc-basic-kind-${idx}`;
                    setFieldError(id, e.error);
                    return;
                }

                // باقي الحقول
                const id = mapUpdateBasic[f];
                if (id) setFieldError(id, e.error);
            });
        }






        // ---- Location actions ----
        if (action === 'edit-location') {
            editingLocationIndex = idx;
            renderAddresses(client.addresses ?? []);
            return;
        }

        if (action === 'cancel-location') {
            editingLocationIndex = null;
            renderAddresses(client.addresses ?? []);
            return;
        }

        if (action === 'save-location') {
            const url = document.getElementById('vcUpdateAddressUrl')?.value;
            if (!url) return toastError('vcUpdateAddressUrl not found', 'Error');

            const addressId = client.addresses?.[idx]?.id;
            if (!addressId) return toastError('AddressId missing.', 'Error');

            clearErrors(`vc-loc-${idx}`);
            const requiredFields = [
                { id: `vc-loc-${idx}-label`, message: 'Address label is required.' },
                { id: `vc-loc-${idx}-fullname`, message: 'Full name or company is required.' },
                { id: `vc-loc-${idx}-country-select`, message: 'Country is required.' },
                { id: `vc-loc-${idx}-streetRaw`, message: 'Street and house number are required.' },
                { id: `vc-loc-${idx}-city`, message: 'City is required.' },
                { id: `vc-loc-${idx}-postal`, message: 'Postal code is required.' },
                { id: `vc-loc-${idx}-line2`, message: 'Address line 2 is required.' }
            ];

            if (!validateRequiredAddressFields(requiredFields)) {
                toastError('All address fields are required.', 'Validation');
                return;
            }

            const selectedCountry = getSelectedCountry(`vc-loc-${idx}-country-select`);

            const payload = {
                customerId: client.id,
                addressId: addressId,
                address: {
                    label: $('vc-loc-' + idx + '-label')?.value?.trim() ?? '',
                    fullNameOrCompany: $('vc-loc-' + idx + '-fullname')?.value?.trim() ?? '',
                    countryCode: selectedCountry.code ?? '',
                    country: selectedCountry.name ?? '',
                    city: $('vc-loc-' + idx + '-city')?.value?.trim() ?? '',
                    postalCode: $('vc-loc-' + idx + '-postal')?.value?.trim() ?? '',
                    streetRaw: $('vc-loc-' + idx + '-streetRaw')?.value?.trim() ?? '',
                    addressLine2: $('vc-loc-' + idx + '-line2')?.value?.trim() ?? '',
                    isDefault: !!client.addresses?.[idx]?.isDefault
                }
            };

            try {
                const updatedRaw = await postJson(url, payload);
                currentClient = normalizeClient(updatedRaw);
                editingLocationIndex = null;
                renderClient(currentClient);
                toastSuccess('Location updated (DB).', 'Success');
            } catch (err) {
                console.error(err);

                if (err?.status === 400 && err?.payload?.errors) {
                    showServerErrors(err.payload.errors, mapUpdateLocation(idx), `vc-loc-${idx}`);
                    toastError('Fix errors.', 'Validation');
                    return;
                }

                toastError(err?.payload?.message || 'Failed to update location.', 'Error');
            }
            return;
        }


        if (action === 'set-default-location') {
            const url = document.getElementById('vcSetDefaultAddressUrl')?.value;
            if (!url) return toastError('vcSetDefaultAddressUrl not found', 'Error');

            const addressId = client.addresses?.[idx]?.id; 
            if (!addressId) return toastError('AddressId missing.', 'Error');

            try {
                const updatedRaw = await postJson(url, { customerId: client.id, addressId });
                currentClient = normalizeClient(updatedRaw);
                renderClient(currentClient);
                toastSuccess('Default updated (DB).', 'Success');
            } catch (err) {
                console.error(err);
                toastError('Failed to set default.', 'Error');
            }
            return;
        }


        if (action === 'delete-location') {
            const url = document.getElementById('vcDeleteAddressUrl')?.value;
            if (!url) return toastError('vcDeleteAddressUrl not found', 'Error');

            const addresses = client.addresses ?? [];
            const targetAddress = addresses[idx];
            const addressId = targetAddress?.id;

            if (!addressId) return toastError('AddressId missing.', 'Error');

            if (addresses.length <= 1) {
                toastError('The last location cannot be deleted.', 'Validation');
                return;
            }

            if (targetAddress.isDefault) {
                toastError('Default location cannot be deleted. Please choose another default location first.', 'Validation');
                return;
            }

            const doDelete = async function () {
                try {
                    const updatedRaw = await postJson(url, { customerId: client.id, addressId });
                    currentClient = normalizeClient(updatedRaw);
                    renderClient(currentClient);
                    toastSuccess('Location deleted (DB).', 'Success');
                } catch (err) {
                    console.error(err);
                    toastError(err?.payload?.message || 'Failed to delete location.', 'Error');
                }
            };

            if (!deleteModalHelper) {
                await doDelete();
                return;
            }

            deleteModalHelper.open(
                'Delete location',
                'Are you sure you want to delete this location?',
                'Delete',
                doDelete
            );
            return;
        }


        // ---- Contact actions (Company only) ----
        if (action === 'edit-contact' || action === 'save-contact' || action === 'cancel-contact'
            || action === 'delete-contact' || action === 'set-primary-contact') {

            if (client.type !== 'Company') {
                toastError('Contacts are available for Company clients only.', 'Error');
                return;
            }
        }

        if (action === 'edit-contact') {
            editingContactIndex = idx;
            renderContacts(client.contacts ?? []);
            return;
        }

        if (action === 'cancel-contact') {
            editingContactIndex = null;
            renderContacts(client.contacts ?? []);
            return;
        }

        if (action === 'save-contact') {
            const url = document.getElementById('vcUpdateContactUrl')?.value;
            if (!url) return toastError('vcUpdateContactUrl not found', 'Error');

            const contactId = client.contacts?.[idx]?.id;
            if (!contactId) return toastError('ContactId missing.', 'Error');

            const payload = {
                customerId: client.id,
                contactId: contactId,
                contact: {
                    salutation: $('vc-c-' + idx + '-salutation')?.value?.trim() || '',
                    firstName: $('vc-c-' + idx + '-firstName')?.value?.trim() || '',
                    lastName: $('vc-c-' + idx + '-lastName')?.value?.trim() || '',
                    position: $('vc-c-' + idx + '-position')?.value?.trim() || '',
                    email: $('vc-c-' + idx + '-email')?.value?.trim() || '',
                    phone: $('vc-c-' + idx + '-phone')?.value?.trim() || '',
                    isPrimary: !!client.contacts?.[idx]?.isPrimary
                }

            };

            if (!validateRequiredCompanyContact(
                payload.contact,
                mapUpdateContact(idx),
                `vc-c-${idx}`)) {
                toastError('All company contact fields are required.', 'Validation');
                return;
            }

            try {
                const updatedRaw = await postJson(url, payload);
                currentClient = normalizeClient(updatedRaw);
                editingContactIndex = null;
                renderClient(currentClient);
                toastSuccess('Contact updated (DB).', 'Success');
            } catch (err) {
                console.error(err);

                if (err?.status === 400 && err?.payload?.errors) {
                    showServerErrors(err.payload.errors, mapUpdateContact(idx), `vc-c-${idx}`);
                    toastError('Fix errors.', 'Validation');
                    return;
                }

                toastError(err?.payload?.message || 'Failed to update contact.', 'Error');
            }
            return;
        }


        if (action === 'set-primary-contact') {
            const url = document.getElementById('vcSetPrimaryContactUrl')?.value;
            if (!url) return toastError('vcSetPrimaryContactUrl not found', 'Error');

            const contactId = client.contacts?.[idx]?.id;
            if (!contactId) return toastError('ContactId missing.', 'Error');

            try {
                const updatedRaw = await postJson(url, { customerId: client.id, contactId });
                currentClient = normalizeClient(updatedRaw);
                renderClient(currentClient);
                toastSuccess('Primary contact updated (DB).', 'Success');
            } catch (err) {
                console.error(err);
                toastError('Failed to set primary contact.', 'Error');
            }
            return;
        }


        if (action === 'delete-contact') {
            const url = document.getElementById('vcDeleteContactUrl')?.value;
            if (!url) return toastError('vcDeleteContactUrl not found', 'Error');

            const contactId = client.contacts?.[idx]?.id;
            if (!contactId) return toastError('ContactId missing.', 'Error');

            const doDelete = async function () {
                try {
                    const updatedRaw = await postJson(url, { customerId: client.id, contactId });
                    currentClient = normalizeClient(updatedRaw);
                    renderClient(currentClient);
                    toastSuccess('Contact deleted (DB).', 'Success');
                } catch (err) {
                    console.error(err);
                    toastError('Failed to delete contact.', 'Error');
                }
            };

            if (!deleteModalHelper) {
                await doDelete();
                return;
            }

            deleteModalHelper.open(
                'Delete contact',
                'Are you sure you want to delete this contact?',
                'Delete',
                doDelete
            );
            return;
        }
    });

    // ---------- Add Location (mock) ----------
    const addLocForm = $('vcAddLocationForm');
    if (addLocForm) {
        addLocForm.addEventListener('submit', async function (e) {
            e.preventDefault();
            if (!currentClient) return;

            const url = document.getElementById('vcAddAddressUrl')?.value;
            if (!url) return toastError('vcAddAddressUrl not found', 'Error');
            clearErrors('vc-add-loc');

            const requiredFields = [
                { id: 'vc-add-loc-label', message: 'Address label is required.' },
                { id: 'vc-add-loc-fullname', message: 'Full name or company is required.' },
                { id: 'vc-add-loc-country-select', message: 'Country is required.' },
                { id: 'vc-add-loc-streetRaw', message: 'Street and house number are required.' },
                { id: 'vc-add-loc-city', message: 'City is required.' },
                { id: 'vc-add-loc-postal', message: 'Postal code is required.' },
                { id: 'vc-add-loc-line2', message: 'Address line 2 is required.' }
            ];

            if (!validateRequiredAddressFields(requiredFields)) {
                toastError('All address fields are required.', 'Validation');
                return;
            }

            const selectedCountry = getSelectedCountry('vc-add-loc-country-select');

            const payload = {
                customerId: currentClient.id,
                address: {
                    label: $('vc-add-loc-label')?.value?.trim() ?? '',
                    fullNameOrCompany: $('vc-add-loc-fullname')?.value?.trim() ?? '',
                    countryCode: selectedCountry.code ?? '',
                    country: selectedCountry.name ?? '',
                    city: $('vc-add-loc-city')?.value?.trim() ?? '',
                    postalCode: $('vc-add-loc-postal')?.value?.trim() ?? '',
                    streetRaw: $('vc-add-loc-streetRaw')?.value?.trim() ?? '',
                    addressLine2: $('vc-add-loc-line2')?.value?.trim() ?? '',
                    isDefault: !!$('vc-add-loc-default')?.checked
                }
            };

            try {
                const updatedRaw = await postJson(url, payload);
                currentClient = normalizeClient(updatedRaw);
                renderClient(currentClient);

                addLocForm.reset();

                initCountryCombo('vc-add-loc-country-combo', 'vc-add-loc-country-menu', 'vc-add-loc-country-select', 'DE');

                const addLocFullName = $('vc-add-loc-fullname');
                if (addLocFullName) addLocFullName.value = currentClient?.name ?? '';

                const c = $('vc-addLocationCollapse');
                if (c) bootstrap.Collapse.getOrCreateInstance(c, { toggle: false }).hide();
                clearErrors('vc-add-loc');
                toastSuccess('Location saved to DB.', 'Success');
            } catch (err) {
                if (err?.status === 400 && err?.payload?.errors) {
                    showServerErrors(err.payload.errors, mapAddLocation, "vc-add-loc");
                    toastError('Fix errors.', 'Validation');
                    return;
                }
                toastError('Failed to add location.', 'Error');
            }

        });
    }




    // ---------- Add Contact (mock) ----------
    const addContactForm = $('vcAddContactForm');
    if (addContactForm) {
        addContactForm.addEventListener('submit', async function (e) {
            e.preventDefault();
            if (!currentClient) return;

            if (currentClient.type !== 'Company') {
                toastError('Contacts are available for Company clients only.', 'Error');
                return;
            }

            const url = document.getElementById('vcAddContactUrl')?.value;
            if (!url) return toastError('vcAddContactUrl not found', 'Error');
            clearErrors('vc-add-c');
            const payload = {
                customerId: currentClient.id,
                contact: {
                    salutation: $('vc-add-c-salutation')?.value?.trim() || '',
                    firstName: $('vc-add-c-firstName')?.value?.trim() || '',
                    lastName: $('vc-add-c-lastName')?.value?.trim() || '',
                    position: $('vc-add-c-position')?.value?.trim() || '',
                    email: $('vc-add-c-email')?.value?.trim() || '',
                    phone: $('vc-add-c-phone')?.value?.trim() || '',
                    isPrimary: !!$('vc-add-c-primary')?.checked
                }

            };

            if (!validateRequiredCompanyContact(
                payload.contact,
                mapAddContact,
                'vc-add-c')) {
                toastError('All company contact fields are required.', 'Validation');
                return;
            }

            try {
                const updatedRaw = await postJson(url, payload);
                currentClient = normalizeClient(updatedRaw);
                renderClient(currentClient);

                addContactForm.reset();
                const c = $('vc-addContactCollapse');
                if (c) bootstrap.Collapse.getOrCreateInstance(c, { toggle: false }).hide();
                clearErrors('vc-add-c');
                toastSuccess('Contact saved to DB.', 'Success');
            } catch (err) {
                console.error(err);

                if (err?.status === 400 && err?.payload?.errors) {
                    showServerErrors(err.payload.errors, mapAddContact, "vc-add-c");
                    toastError('Fix errors.', 'Validation');
                    return;
                }

                toastError(err?.payload?.message || 'Failed to add contact.', 'Error');
            }
        });
    }


    

    // ---------- Table Delete (server) ----------
    document.addEventListener('click', async function (e) {
        const btn = e.target.closest('[data-vc-action="table-delete"]');
        if (!btn) return;

        e.preventDefault();

        const id = btn.getAttribute('data-client-id');
        const hid = document.getElementById('tblDeleteClientId');
        const form = document.getElementById('tblDeleteForm');
        const infoBaseUrl = document.getElementById('vcDeleteClientInfoUrl')?.value;
        if (!id || !hid || !form) return;

        btn.disabled = true;

        try {
            let info = null;

            if (infoBaseUrl) {
                const infoUrl = `${infoBaseUrl}${infoBaseUrl.includes('?') ? '&' : '?'}clientId=${encodeURIComponent(id)}`;
                const response = await fetch(infoUrl, {
                    method: 'GET',
                    headers: { 'Accept': 'application/json' },
                    credentials: 'same-origin'
                });

                if (response.ok) {
                    info = await response.json();
                }
            }

            // The client has projects: explain why deletion is blocked and do not
            // show a Delete confirmation button.
            if (info && info.canDelete === false) {
                const projectPreview = Array.isArray(info.projectNames) && info.projectNames.length
                    ? ` Projects: ${info.projectNames.join(', ')}${info.projectCount > info.projectNames.length ? ', ...' : ''}`
                    : '';

                const blockedMessage = `${info.message || 'This client cannot be deleted because it has linked projects and related data.'}${projectPreview}`;

                if (deleteModalHelper) {
                    deleteModalHelper.open(
                        'Delete not allowed',
                        blockedMessage,
                        'Delete',
                        null,
                        { hideConfirm: true }
                    );
                } else {
                    toastError(blockedMessage, 'Delete not allowed');
                }
                return;
            }

            const customerName = info?.clientName ? ` "${info.clientName}"` : '';
            const confirmMessage = info?.message || `Are you sure you want to delete this client${customerName}?`;

            if (!deleteModalHelper) {
                hid.value = id;
                form.submit();
                return;
            }

            deleteModalHelper.open(
                'Delete client',
                confirmMessage,
                'Delete',
                async function () {
                    hid.value = id;
                    form.submit();
                }
            );
        } catch (err) {
            console.error('Delete client check failed:', err);

            // Even when this preliminary check fails, the server-side handler and
            // service still prevent deletion of a client that owns projects.
            if (!deleteModalHelper) {
                toastError('Unable to verify whether this client can be deleted.', 'Error');
                return;
            }

            deleteModalHelper.open(
                'Delete client',
                'Could not verify linked projects. Continue with the delete request?',
                'Delete',
                async function () {
                    hid.value = id;
                    form.submit();
                }
            );
        } finally {
            btn.disabled = false;
        }
    });


    // ---------- Server Fetch ----------
    async function fetchClientById(id) {
        const baseUrl = document.getElementById('vcClientUrl')?.value;
        if (!baseUrl) throw new Error('vcClientUrl not found');

        const url = `${baseUrl}${baseUrl.includes('?') ? '&' : '?'}id=${encodeURIComponent(id)}`;

        const res = await fetch(url, {
            method: 'GET',
            headers: { 'Accept': 'application/json' },
            credentials: 'same-origin'
        });

        if (!res.ok) {
            const text = await res.text();
            throw new Error(`HTTP ${res.status}: ${text}`);
        }

        const data = await res.json();
        return normalizeClient(data);
    }
    async function postJson(url, body) {
        const token = document.getElementById('antiForgeryToken')?.value;

        const res = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json',
                ...(token ? { 'RequestVerificationToken': token } : {})
            },
            body: JSON.stringify(body)
        });

        const contentType = res.headers.get('content-type') || '';
        const isJson = contentType.includes('application/json');

        if (!res.ok) {
            let payload = null;

            try {
                payload = isJson ? await res.json() : { message: await res.text() };
            } catch {
                payload = { message: 'Request failed.' };
            }

            // نرمي Error “منظم” بدل string
            throw { status: res.status, payload };
        }

        return await res.json();
    }
    function clearErrors(prefix) {
        document.querySelectorAll(`[id^="err-${prefix}"]`).forEach(el => el.textContent = '');
    }

    function setFieldError(elId, message) {
        const el = document.getElementById(`err-${elId}`);
        if (el) el.textContent = message || '';
    }

    function validateRequiredAddressFields(fields) {
        let firstInvalid = null;

        (fields || []).forEach(field => {
            const el = document.getElementById(field.id);
            const value = (el?.value ?? '').toString().trim();
            const isValid = value.length > 0;

            setFieldError(field.id, isValid ? '' : field.message);

            if (!isValid && !firstInvalid) {
                firstInvalid = document.getElementById(field.focusId || field.id);
            }
        });

        if (firstInvalid) {
            firstInvalid.focus?.();
            return false;
        }

        return true;
    }

    function markAddressInputsRequired(root = document) {
        const ids = [
            'vc-add-loc-label',
            'vc-add-loc-fullname',
            'vc-add-loc-country-combo',
            'vc-add-loc-streetRaw',
            'vc-add-loc-city',
            'vc-add-loc-postal',
            'vc-add-loc-line2'
        ];

        ids.forEach(id => {
            const el = root.querySelector?.(`#${id}`) || document.getElementById(id);
            if (!el) return;
            el.required = true;
            el.setAttribute('aria-required', 'true');
        });

        const createModal = document.getElementById('FormModal');
        if (!createModal) return;

        [
            'Address.Label',
            'Address.FullNameOrCompany',
            'Address.StreetRaw',
            'Address.AddressLine2',
            'Address.PostalCode',
            'Address.City'
        ].forEach(name => {
            const el = createModal.querySelector(`[name="${name}"]`);
            if (!el || el.type === 'hidden') return;
            el.required = true;
            el.setAttribute('aria-required', 'true');
        });

        const createCountryCombo = createModal.querySelector('#create-country-combo');
        if (createCountryCombo) {
            createCountryCombo.required = true;
            createCountryCombo.setAttribute('aria-required', 'true');
        }
    }

    

    function showServerErrors(errors, map, prefixToClear) {
        if (prefixToClear) clearErrors(prefixToClear);
        (errors || []).forEach(e => {
            const id = map[e.field];
            if (id) setFieldError(id, e.error);
        });
    }


    function normalizeClient(data) {
        if (!data) return null;

        const normalizeType = (v) => {
            if (v === null || v === undefined) return 'Individual';
            if (typeof v === 'number') return v === 1 ? 'Company' : 'Individual';
            const s = String(v).trim().toLowerCase();
            if (s === 'company' || s.includes('comp')) return 'Company';
            return 'Individual';
        };

        // إذا السيرفر يرجع Wrapper مثل { customer: {...} } أو { Customer: {...} }
        const root = data.customer ?? data.Customer ?? data;

        const rawAddresses = root.addresses ?? root.Addresses ?? [];
        const rawContacts =
            root.contacts ?? root.Contacts ??
            (root.contact ? [root.contact] : []) ??
            (root.Contact ? [root.Contact] : []);
        const rawProjects =
            root.projects ?? root.Projects ??
            data.projects ?? data.Projects ??
            data.customer?.projects ?? data.Customer?.Projects ??
            [];

        const normProjects = (rawProjects || []).map(p => ({
            id: p.id ?? p.Id ?? p.projectId ?? p.ProjectId ?? null,
            title: p.title ?? p.Title ?? p.name ?? p.Name ?? '',
            status: p.status ?? p.Status ?? p.projectStatus ?? p.ProjectStatus ?? '',
            startDate: p.startDate ?? p.StartDate ?? p.start ?? p.Start ?? null,
            endDate: p.endDate ?? p.EndDate ?? p.end ?? p.End ?? null
        }));
        const normalizeLexware = (v) => {
            if (v === null || v === undefined) return null; // لا تفرض NotExported
            if (typeof v === 'number') return v === 0 ? 'Imported' : v === 1 ? 'Exported' : 'NotExported';

            const s = String(v).trim().toLowerCase();
            if (s === 'imported') return 'Imported';
            if (s === 'exported') return 'Exported';
            if (s === 'notexported' || s === 'not_exported' || s === 'not exported') return 'NotExported';
            return String(v);
        };
        const rawEmails = root.emailAddresses ?? root.EmailAddresses ?? [];
        const primaryEmail =
            (root.email ?? root.Email) ||
            (rawEmails[0]?.email ?? rawEmails[0]?.Email) ||
            '';
        return {
            id: root.id ?? root.Id,
            type: normalizeType(root.type ?? root.Type),
            name: root.name ?? root.Name,
            firstName: root.firstName ?? root.FirstName ?? '',
            lastName: root.lastName ?? root.LastName ?? '',
            email: primaryEmail,
            emailAddresses: (rawEmails || []).map(e => ({
                id: e.id ?? e.Id ?? null,
                kind: e.kind ?? e.Kind ?? 'business',
                email: e.email ?? e.Email ?? ''
            })),
            phone: root.phone ?? root.Phone,
            taxId: root.taxId ?? root.TaxId,
            notes: root.notes ?? root.Notes,
            lexwareType: normalizeLexware(root.lexwareType ?? root.LexwareType),

            lexwareCustomerNumber: root.lexwareCustomerNumber ?? root.LexwareCustomerNumber ?? null,
            lexwareContactId: root.lexwareContactId ?? root.LexwareContactId ?? null,
            lexwareOrganizationId: root.lexwareOrganizationId ?? root.LexwareOrganizationId ?? null,
            lexwareVersion: root.lexwareVersion ?? root.LexwareVersion ?? null,
            lexwareArchived: root.lexwareArchived ?? root.LexwareArchived ?? null,
            lexwareAllowTaxFreeInvoices: root.lexwareAllowTaxFreeInvoices ?? root.LexwareAllowTaxFreeInvoices ?? null,
            lexwareSyncedAtUtc: root.lexwareSyncedAtUtc ?? root.LexwareSyncedAtUtc ?? null,

            addresses: (rawAddresses || []).map(a => ({
                id: a.id ?? a.Id,   // ✅
                label: a.label ?? a.Label ?? 'Location',
                fullNameOrCompany: a.fullNameOrCompany ?? a.FullNameOrCompany ?? '',
                isDefault: a.isDefault ?? a.IsDefault ?? a.Default ?? false,
                
                city: a.city ?? a.City ?? '',
                country: a.country ?? a.Country ?? '',
                countryCode: a.countryCode ?? a.CountryCode ?? '',
                streetRaw: a.streetRaw ?? a.StreetRaw ?? '',

                postalCode: a.postalCode ?? a.PostalCode ?? '',
                addressLine2: a.addressLine2 ?? a.AddressLine2 ?? ''
            })),
            contacts: (rawContacts || []).map(c => ({
                id: c.id ?? c.Id,
                isPrimary: c.isPrimary ?? c.IsPrimary ?? c.Primary ?? false,

                salutation: c.salutation ?? c.Salutation ?? '',
                firstName: c.firstName ?? c.FirstName ?? '',
                lastName: c.lastName ?? c.LastName ?? '',

                name: c.name ?? c.Name ?? '',
                position: c.position ?? c.Position ?? '',
                email: c.email ?? c.Email ?? '',
                phone: c.phone ?? c.Phone ?? ''
            })),
            


            projects: normProjects
        };
    }
    function fmtBool(v) {
        if (v === null || v === undefined) return '—';
        return v ? 'Yes' : 'No';
    }
    function fmtDate(v) {
        if (!v) return '—';
        const d = new Date(v);
        if (isNaN(d.getTime())) return String(v);
        return d.toISOString().replace('T', ' ').replace('Z', ' UTC');
    }
    function emailChipHtml(kind, email) {
        const k = (kind || 'other').toLowerCase();
        const cls = k === 'business'
            ? 'badge bg-primary bg-opacity-10 text-primary'
            : k === 'private'
                ? 'badge bg-success bg-opacity-10 text-success'
                : 'badge bg-secondary bg-opacity-10 text-secondary';
        return `<span class="d-inline-flex align-items-center gap-2 px-2 py-1 rounded-3 border border-opacity-25">
        <span class="${cls}">${esc(k)}</span>
        <span>${esc(email || '—')}</span>
    </span>`;
    }
    function renderEmailListView(list) {
        const wrap = $('vc-emailList');
        if (!wrap) return;
        const arr = (list || []).filter(x => (x?.email || '').trim().length > 0);
        if (!arr.length) { wrap.innerHTML = `<span class="text-muted">—</span>`; return; }
        wrap.innerHTML = arr.map(x => emailChipHtml(x.kind, x.email)).join('');
    }

    // ---------- Create Modal: Emails (multi) ----------
    function renumberCreateEmailRows(modalEl) {
        const rows = modalEl.querySelectorAll('#create-email-list [data-email-row="create"]');

        rows.forEach((row, i) => {
            const kindSel = row.querySelector('select');
            const emailInp = row.querySelector('input[type="email"]');
            const kindMsg = row.querySelector('[data-valmsg-for$=".Kind"]');
            const emailMsg = row.querySelector('[data-valmsg-for$=".Email"]');

            // names (important for ASP.NET model binder)
            const kindName = `Customer.EmailAddresses[${i}].Kind`;
            const emailName = `Customer.EmailAddresses[${i}].Email`;

            if (kindSel) {
                kindSel.name = kindName;
                kindSel.id = `Customer_EmailAddresses_${i}__Kind`;
            }
            if (emailInp) {
                emailInp.name = emailName;
                emailInp.id = `Customer_EmailAddresses_${i}__Email`;
            }

            // validation message hook (unobtrusive)
            if (kindMsg) kindMsg.setAttribute('data-valmsg-for', kindName);
            if (emailMsg) emailMsg.setAttribute('data-valmsg-for', emailName);
        });

        // disable remove if only one
        const removeButtons = modalEl.querySelectorAll('.create-email-remove');
        removeButtons.forEach(btn => btn.disabled = rows.length <= 1);
    }

    function createEmailRowDom(i, kind = 'business', email = '') {
        const row = document.createElement('div');
        row.className = 'email-row';
        row.setAttribute('data-email-row', 'create');

        row.innerHTML = `
        <div class="input-group">
            <select class="form-select" style="max-width:170px">
                <option value="business">business</option>
                <option value="private">private</option>
                <option value="other">other</option>
            </select>

            <input class="form-control" type="email" placeholder="name@domain.com" required />

            <button type="button"
                    class="btn btn-outline-danger create-email-remove"
                    title="Remove email">
                <i class="ri-delete-bin-line align-middle" style="font-size:18px"></i>
            </button>
        </div>

        <div class="row g-2 mt-1">
            <div class="col-12 col-md-4">
                <span class="text-danger small field-validation-valid"
                      data-valmsg-for="Customer.EmailAddresses[${i}].Kind"
                      data-valmsg-replace="true"></span>
            </div>
            <div class="col-12 col-md-8">
                <span class="text-danger small field-validation-valid"
                      data-valmsg-for="Customer.EmailAddresses[${i}].Email"
                      data-valmsg-replace="true"></span>
            </div>
        </div>
    `;

        const sel = row.querySelector('select');
        const inp = row.querySelector('input[type="email"]');
        if (sel) sel.value = kind;
        if (inp) inp.value = email;

        return row;
    }


    // Init for FormModal
    document.addEventListener('DOMContentLoaded', function () {
        const modalEl = document.getElementById('FormModal');
        if (!modalEl) return;

        const addBtn = modalEl.querySelector('#create-email-add');
        const list = modalEl.querySelector('#create-email-list');
        if (!addBtn || !list) return;

        // remove handler (delegated)
        modalEl.addEventListener('click', function (e) {
            const rm = e.target.closest('.create-email-remove');
            if (!rm) return;

            const rows = modalEl.querySelectorAll('#create-email-list [data-email-row="create"]');
            if (rows.length <= 1) return; // keep at least one
            rm.closest('[data-email-row="create"]')?.remove();
            renumberCreateEmailRows(modalEl);

            // reparse unobtrusive validation if exists
            if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
                const form = modalEl.querySelector('form');
                if (form) window.jQuery.validator.unobtrusive.parse(form);
            }
        });

        addBtn.addEventListener('click', function () {
            const idx = modalEl.querySelectorAll('#create-email-list [data-email-row="create"]').length;
            list.appendChild(createEmailRowDom(idx));
            renumberCreateEmailRows(modalEl);

            if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
                const form = modalEl.querySelector('form');
                if (form) window.jQuery.validator.unobtrusive.parse(form);
            }
        });

        // first run (on open/initial)
        renumberCreateEmailRows(modalEl);

    });
    
    // ---------- Emails (View + Edit in VC) ----------
    function emailBadge(kind, email) {
        const k = (kind || 'business').toLowerCase();
        const cls =
            k === 'business' ? 'badge bg-primary bg-opacity-10 text-primary' :
                k === 'private' ? 'badge bg-success bg-opacity-10 text-success' :
                    'badge bg-secondary bg-opacity-10 text-secondary';

        return `<span class="${cls}">${esc(k)}</span> <span class="text-muted">${esc(email)}</span>`;
    }

    function renderEmailsView(emails) {
        const wrap = $('vc-emailList');
        if (!wrap) return;

        const list = (emails || []).filter(x => (x?.email ?? '').trim().length > 0);
        if (!list.length) {
            wrap.innerHTML = `<span class="text-muted">—</span>`;
            return;
        }

        wrap.innerHTML = list.map(e => `
        <span class="d-inline-flex align-items-center gap-2 px-2 py-1 rounded-3 border border-secondary border-opacity-25">
            ${emailBadge(e.kind, e.email)}
        </span>
    `).join('');
    }

    
    document.addEventListener('click', function (e) {
        if (e.target.closest('#vc-basic-email-add')) {
            e.preventDefault();
            const existing = collectEmailEditRows();
            existing.push({ id: null, kind: 'business', email: '' });
            renderEmailEditRows(existing.length ? existing : [{ kind: 'business', email: '' }]);
            return;
        }

        const rm = e.target.closest('.vc-basic-email-remove');
        if (rm) {
            e.preventDefault();
            const rows = Array.from(document.querySelectorAll('[data-email-row="basic"]'));
            if (rows.length <= 1) return; // لا تحذف آخر واحد

            const row = rm.closest('[data-email-row="basic"]');
            row?.remove();

            // إعادة فهرسة (لأننا نعتمد IDs على index)
            const after = collectEmailEditRows();
            renderEmailEditRows(after.length ? after : [{ kind: 'business', email: '' }]);
            return;
        }
    });
    // ---------- Lexware: Refresh/Import Contacts ----------
    document.addEventListener('click', async function (e) {
        const btn = e.target.closest('[data-vc-action="lexware-refresh"]');
        if (!btn) return;

        e.preventDefault();

        const url = document.getElementById('vcLexwareImportUrl')?.value;
        if (!url) {
            toastError('vcLexwareImportUrl not found.', 'Lexware');
            return;
        }

        try {
            btn.disabled = true;
            toastInfo('Importing contacts from Lexware...', 'Lexware');

            const res = await postJson(url, {}); // Razor Page handler returns JSON

            const created = res?.created ?? 0;
            const skipped = res?.skipped ?? 0;
            const failed = res?.failed ?? 0;

            const msg = `Done. Created: ${created}, Skipped: ${skipped}, Failed: ${failed}`;

            sessionStorage.setItem('lx_toast', JSON.stringify({
                type: 'success',
                title: 'Lexware',
                message: msg
            }));

            window.location.reload();
        } catch (err) {
            console.error(err);
            toastError(err?.payload?.message || 'Lexware import failed.', 'Lexware');
        } finally {
            btn.disabled = false;
        }
    });
    document.addEventListener('click', async function (e) {
        const btn = e.target.closest('[data-vc-action="table-export"]');
        if (!btn) return;

        e.preventDefault();

        const id = btn.dataset.clientId;
        const url = document.getElementById('vcLexwareExportUrl')?.value;
        if (!url) return toastError("Export url not found", "Lexware");

        try {
            btn.disabled = true;
            toastInfo("Exporting to Lexware...", "Lexware");

            await postJson(url, { customerId: id });

            sessionStorage.setItem('lx_toast', JSON.stringify({
                type: "success",
                title: "Lexware",
                message: "Exported successfully."
            }));

            window.location.reload();
        } catch (err) {
            console.error(err);
            toastError(err?.payload?.message || "Export failed.", "Lexware");
        } finally {
            btn.disabled = false;
        }
    });
    function toggleBasicNameFields(clientType) {
        const isCompany = clientType === "Company";

        document.querySelectorAll(".vc-individual-only").forEach(el => {
            el.classList.toggle("d-none", isCompany);
        });

        document.querySelectorAll(".vc-company-only").forEach(el => {
            el.classList.toggle("d-none", !isCompany);
        });
    }
    

    function captureSearchState(input) {
        const hadFocus = document.activeElement === input;

        return {
            hadFocus,
            value: input?.value ?? '',
            start: hadFocus && typeof input?.selectionStart === 'number' ? input.selectionStart : null,
            end: hadFocus && typeof input?.selectionEnd === 'number' ? input.selectionEnd : null
        };
    }

    function restoreSearchState(tableCardId, state) {
        if (!state?.hadFocus) return;

        requestAnimationFrame(function () {
            const host = document.getElementById(tableCardId);
            const input = host?.querySelector('.order-search input[name="q"]');
            if (!input) return;

            input.focus({ preventScroll: true });

            const valueLength = input.value.length;
            const start = Math.min(state.start ?? valueLength, valueLength);
            const end = Math.min(state.end ?? valueLength, valueLength);

            if (typeof input.setSelectionRange === 'function') {
                input.setSelectionRange(start, end);
            }
        });
    }

    (function bindClientsLiveSearch() {
        let debounceTimer = null;
        let activeController = null;

        function initClientsLiveSearch() {
            const host = document.getElementById('clientsTableCard');
            if (!host) return;

            const form = host.querySelector('.order-search');
            const input = form?.querySelector('input[name="q"]');
            if (!form || !input) return;
            initSearchClearButtons(host);
            if (form.dataset.liveSearchBound === '1') return;
            form.dataset.liveSearchBound = '1';

            async function reloadClientsTable() {
                const currentHost = document.getElementById('clientsTableCard');
                if (!currentHost) return;

                const currentForm = currentHost.querySelector('.order-search');
                const currentInput = currentForm?.querySelector('input[name="q"]');
                if (!currentForm || !currentInput) return;

                const searchState = captureSearchState(currentInput);

                const url = new URL(window.location.href);
                const q = currentInput.value.trim();

                if (q) url.searchParams.set('q', q);
                else url.searchParams.delete('q');

                url.searchParams.set('p', '1');

                const pageSizeInput = currentForm.querySelector('input[name="pageSize"]');
                if (pageSizeInput?.value) {
                    url.searchParams.set('pageSize', pageSizeInput.value);
                }

                try {
                    if (activeController) activeController.abort();
                    activeController = new AbortController();

                    const res = await fetch(url.toString(), {
                        method: 'GET',
                        headers: { 'X-Requested-With': 'XMLHttpRequest' },
                        signal: activeController.signal
                    });

                    if (!res.ok) throw new Error(`HTTP ${res.status}`);

                    const html = await res.text();
                    const doc = new DOMParser().parseFromString(html, 'text/html');
                    const newHost = doc.getElementById('clientsTableCard');
                    if (!newHost) return;

                    currentHost.outerHTML = newHost.outerHTML;

                    window.history.replaceState({}, '', url.pathname + url.search);

                    initClientsLiveSearch();
                    restoreSearchState('clientsTableCard', searchState);
                } catch (err) {
                    if (err.name === 'AbortError') return;
                    console.error('Clients live search failed:', err);
                }
            }

            input.addEventListener('input', function () {
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(reloadClientsTable, 500);
            });

            form.addEventListener('submit', function (e) {
                e.preventDefault();
                clearTimeout(debounceTimer);
                reloadClientsTable();
            });
        }

        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', initClientsLiveSearch);
        } else {
            initClientsLiveSearch();
        }
    })();







})();


