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
        "Address.CountryCode": "vc-add-loc-countryCode",   
        "Address.Country": "vc-add-loc-country",          
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

    function mapUpdateLocation(idx) {
        return {
            "Address.Label": `vc-loc-${idx}-label`,
            "Address.CountryCode": `vc-loc-${idx}-countryCode`,
            "Address.Country": `vc-loc-${idx}-country`,
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
    function setText(id, value) { const el = $(id); if (el) el.textContent = value ?? '—'; }
    function setHtml(id, html) { const el = $(id); if (el) el.innerHTML = html ?? ''; }

    function toastSuccess(msg, title) { UI?.toast?.success ? UI.toast.success(msg, title) : alert((title ? title + ": " : "") + msg); }
    function toastInfo(msg, title) { UI?.toast?.info ? UI.toast.info(msg, title) : alert((title ? title + ": " : "") + msg); }
    function toastError(msg, title) { UI?.toast?.error ? UI.toast.error(msg, title) : alert((title ? title + ": " : "") + msg); }

    async function confirmBox(message, title) {
        if (UI?.confirm?.basic) return await UI.confirm.basic(message, { title: title ?? 'Confirm', okText: 'Yes', cancelText: 'No' });
        return window.confirm(message);
    }

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


    // ---------- Mock Data (demo) ----------
    const mockClients = {
        "demo-individual": {
            id: "demo-individual",
            type: "Individual",
            name: "Anas Sadek",
            email: "anas@email.com",
            phone: "+49 174 234 5678",
            taxId: "—",
            notes: "VIP customer, prefers email.",
            addresses: [
                { label: "Home", isDefault: true, street: "Alexanderplatz", streetNr: "1", city: "Berlin", country: "Germany", postalCode: "10178", addressLine2: "" }
            ],
            contacts: [],
            projects: [
                { name: "Website Redesign", status: "Active" },
                { name: "Maintenance", status: "Planned" }
            ]
        },
        "demo-company": {
            id: "demo-company",
            type: "Company",
            name: "ACME LLC",
            email: "finance@acme.com",
            phone: "+1 212 555 0199",
            taxId: "CR-123456",
            notes: "Company account. Multiple locations.",
            addresses: [
                { label: "Billing", isDefault: true, street: "5th Avenue", streetNr: "500", city: "New York", country: "USA", postalCode: "10018", addressLine2: "Floor 12" },
                { label: "Shipping", isDefault: false, street: "Industrial Rd", streetNr: "77", city: "Newark", country: "USA", postalCode: "07102", addressLine2: "" }
            ],
            contacts: [
                { isPrimary: true, name: "Sara Ahmed", position: "Manager", email: "sara@acme.com", phone: "+1 212 555 0101" },
                { isPrimary: false, name: "John Finch", position: "Finance", email: "john@acme.com", phone: "+1 212 555 0102" }
            ],
            projects: [
                { name: "ERP Integration", status: "Active" },
                { name: "Invoice Portal", status: "Planned" }
            ]
        }
    };

    // ---------- Modal Session State ----------
    let currentClientId = null;
    let currentClient = null;
    let editingLocationIndex = null;
    let editingContactIndex = null;
    let editingBasic = false;

    // ---------- Render Projects ----------
    function renderProjects(list) {
        const body = $('vc-projects');
        const countEl = $('vc-projectCount');
        if (countEl) countEl.textContent = (list?.length ?? 0);
        if (!body) return;

        if (!list || !list.length) {
            body.innerHTML = `<tr><td colspan="3" class="text-muted">No projects.</td></tr>`;
            return;
        }

        body.innerHTML = list.map(p => `
                <tr>
                    <td class="fw-semibold">${esc(p.name)}</td>
                    <td><span class="badge bg-secondary bg-opacity-10 text-secondary">${esc(p.status)}</span></td>
                    <td class="text-end">
                        <button type="button" class="btn p-0 border-0 bg-transparent text-secondary" title="View">
                            <i class="material-icons-outlined">visibility</i>
                        </button>
                    </td>
                </tr>
            `).join('');
    }

    // ---------- Render Locations (inline edit) ----------
    function renderAddresses(list) {
        const wrap = $('vc-addressList');
        const count = $('vc-addressCount');
        if (count) count.textContent = (list?.length ?? 0);
        if (!wrap) return;

        if (!list || !list.length) {
            wrap.innerHTML = `<div class="text-muted">No locations.</div>`;
            return;
        }

        wrap.innerHTML = list.map((a, idx) => {
            const isEditing = (editingLocationIndex === idx);

            // support old/mock keys too
            const streetRaw = a.streetRaw ?? a.street ?? '';
            const addressLine2 = a.addressLine2 ?? a.AddressLine2 ?? '';
            const postalCode = a.postalCode ?? a.PostalCode ?? '';
            const city = a.city ?? a.City ?? '';
            const country = a.country ?? a.Country ?? '';
            const countryCode = a.countryCode ?? a.CountryCode ?? '';
            const label = a.label ?? a.Label ?? 'Location';

            const line1 = streetRaw ?? '';
            const line2 = addressLine2 ? ` • ${esc(addressLine2)}` : '';
            const addressText = (line1 || addressLine2) ? `${esc(line1)}${line2}` : '—';

            const cityText = `${esc(postalCode ?? '')} ${esc(city ?? '')}${(city || country) ? ', ' : ''}${esc(country ?? '')}`.trim() || '—';

            const isDefault = !!(a.isDefault ?? a.IsDefault ?? a.Default);
            const defaultBadge = isDefault ? `<span class="badge bg-primary bg-opacity-10 text-primary ms-2">Default</span>` : '';
            const starIcon = isDefault ? 'star' : 'star_border';

            return `
            <div class="card rounded-4 border bg-transparent shadow-none mb-0">
                <div class="card-body py-3">
                    <div class="d-flex align-items-start justify-content-between gap-3">

                        <div class="flex-grow-1">

                            <!-- VIEW -->
                            <div class="${isEditing ? 'd-none' : ''}">
                                <div class="fw-semibold">
                                    ${esc(label)}
                                    ${defaultBadge}
                                </div>
                                <div class="text-muted small">${addressText}</div>
                                <div class="text-muted small">${cityText}</div>
                            </div>

                            <!-- EDIT (inline) -->
                            <div class="${isEditing ? '' : 'd-none'}">
                                <div class="row g-2">

                                    <div class="col-12 col-md-4">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-label">
                                            Label
                                            <span class="material-icons-outlined text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="Example: Billing / Shipping / Home">info</span>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-label"
                                               value="${esc(label ?? '')}"
                                               placeholder="Billing" />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-label"></div>
                                    </div>

                                    <div class="col-12 col-md-4">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-country">
                                            Country
                                            <span class="material-icons-outlined text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="Country name (optional). Example: Germany">info</span>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-country"
                                               value="${esc(country ?? '')}"
                                               placeholder="Germany" />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-country"></div>
                                    </div>

                                    <div class="col-12 col-md-4">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-city">City</label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-city"
                                               value="${esc(city ?? '')}"
                                               placeholder="Berlin" />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-city"></div>
                                    </div>

                                    <div class="col-12 col-md-4">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-countryCode">
                                            Country Code
                                            <span class="material-icons-outlined text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="ISO 2-letter code. Example: DE, US, NL">info</span>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-countryCode"
                                               value="${esc(countryCode ?? '')}"
                                               placeholder="DE" />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-countryCode"></div>
                                    </div>

                                    <div class="col-12 col-md-8">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-streetRaw">
                                            Street (raw)
                                            <span class="material-icons-outlined text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="Street and number. Example: Hauptstr. 12">info</span>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-streetRaw"
                                               value="${esc(streetRaw ?? '')}"
                                               placeholder="Hauptstr. 12" />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-streetRaw"></div>
                                    </div>

                                    <div class="col-12 col-md-4">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-postal">Postal Code</label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-postal"
                                               value="${esc(postalCode ?? '')}"
                                               placeholder="10115" />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-postal"></div>
                                    </div>

                                    <div class="col-12">
                                        <label class="form-label small mb-1" for="vc-loc-${idx}-line2">
                                            Address Line 2
                                            <span class="material-icons-outlined text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="Supplement, floor, apartment, etc.">info</span>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-loc-${idx}-line2"
                                               value="${esc(addressLine2 ?? '')}"
                                               placeholder="Floor 2, Apt 5" />
                                        <div class="text-danger small mt-1" id="err-vc-loc-${idx}-line2"></div>
                                    </div>

                                </div>
                            </div>

                        </div>

                        <!-- Actions -->
                        <div class="d-flex align-items-start gap-3">

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-primary"
                                    title="Set default"
                                    data-vc-action="set-default-location"
                                    data-index="${idx}">
                                <i class="material-icons-outlined">${starIcon}</i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-info ${isEditing ? 'd-none' : ''}"
                                    title="Edit"
                                    data-vc-action="edit-location"
                                    data-index="${idx}">
                                <i class="material-icons-outlined">edit</i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-success ${isEditing ? '' : 'd-none'}"
                                    title="Save"
                                    data-vc-action="save-location"
                                    data-index="${idx}">
                                <i class="material-icons-outlined">check</i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-muted ${isEditing ? '' : 'd-none'}"
                                    title="Cancel"
                                    data-vc-action="cancel-location"
                                    data-index="${idx}">
                                <i class="material-icons-outlined">close</i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-danger"
                                    title="Delete"
                                    data-vc-action="delete-location"
                                    data-index="${idx}">
                                <i class="material-icons-outlined">delete</i>
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
            const starIcon = isPrimary ? 'star' : 'star_border';

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
                                    <span class="material-icons-outlined align-middle me-1" style="font-size:18px">send</span>${esc(email || '—')}
                                </div>
                                <div class="text-muted small">
                                    <span class="material-icons-outlined align-middle me-1" style="font-size:18px">call</span>${esc(phone || '—')}
                                </div>
                            </div>

                            <!-- EDIT -->
                            <div class="${isEditing ? '' : 'd-none'}">
                                <div class="row g-2">

                                    <div class="col-12 col-md-3">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-salutation">
                                            Salutation
                                            <span class="material-icons-outlined text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="Example: Herr / Frau / Mr / Ms">info</span>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-salutation"
                                               value="${esc(salutation ?? '')}"
                                               placeholder="Herr" />
                                        <div class="text-danger small mt-1" id="err-vc-c-${idx}-salutation"></div>
                                    </div>

                                    <div class="col-12 col-md-4">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-firstName">First Name</label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-firstName"
                                               value="${esc(firstName ?? '')}"
                                               placeholder="John" />
                                        <div class="text-danger small mt-1" id="err-vc-c-${idx}-firstName"></div>
                                    </div>

                                    <div class="col-12 col-md-5">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-lastName">Last Name</label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-lastName"
                                               value="${esc(lastName ?? '')}"
                                               placeholder="Doe" />
                                        <div class="text-danger small mt-1" id="err-vc-c-${idx}-lastName"></div>
                                    </div>

                                    <div class="col-12 col-md-6">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-position">
                                            Position
                                            <span class="material-icons-outlined text-muted ms-1"
                                                  style="font-size:16px"
                                                  data-bs-toggle="tooltip"
                                                  title="Job title inside the company">info</span>
                                        </label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-position"
                                               value="${esc(position ?? '')}"
                                               placeholder="Manager" />
                                        <div class="text-danger small mt-1" id="err-vc-c-${idx}-position"></div>
                                    </div>

                                    <div class="col-12 col-md-6">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-email">Email</label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-email"
                                               value="${esc(email ?? '')}"
                                               placeholder="name@domain.com" />
                                        <div class="text-danger small mt-1" id="err-vc-c-${idx}-email"></div>
                                    </div>

                                    <div class="col-12 col-md-6">
                                        <label class="form-label small mb-1" for="vc-c-${idx}-phone">Phone</label>
                                        <input class="form-control form-control-sm"
                                               id="vc-c-${idx}-phone"
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
                                <i class="material-icons-outlined">${starIcon}</i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-info ${isEditing ? 'd-none' : ''}"
                                    title="Edit"
                                    data-vc-action="edit-contact"
                                    data-index="${idx}">
                                <i class="material-icons-outlined">edit</i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-success ${isEditing ? '' : 'd-none'}"
                                    title="Save"
                                    data-vc-action="save-contact"
                                    data-index="${idx}">
                                <i class="material-icons-outlined">check</i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-muted ${isEditing ? '' : 'd-none'}"
                                    title="Cancel"
                                    data-vc-action="cancel-contact"
                                    data-index="${idx}">
                                <i class="material-icons-outlined">close</i>
                            </button>

                            <button type="button"
                                    class="btn p-0 border-0 bg-transparent text-danger"
                                    title="Delete"
                                    data-vc-action="delete-contact"
                                    data-index="${idx}">
                                <i class="material-icons-outlined">delete</i>
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
    document.addEventListener('DOMContentLoaded', () => {
        const typeSelect = document.getElementById("type");
        const firstName = document.getElementById("firstName");
        const lastName = document.getElementById("lastName");
        const hiddenName = document.getElementById("hiddenName");
        const companyName = document.getElementById("companyName");

        function updateUI() {
            const isCompany = typeSelect.value === "Company";

            document.querySelectorAll(".individual-only").forEach(x => x.classList.toggle("d-none", isCompany));
            document.querySelectorAll(".company-only").forEach(x => x.classList.toggle("d-none", !isCompany));

            if (!isCompany) {
                companyName?.removeAttribute("required");
                firstName?.setAttribute("required", "required");
                lastName?.setAttribute("required", "required");
            } else {
                companyName?.setAttribute("required", "required");
                firstName?.removeAttribute("required");
                lastName?.removeAttribute("required");
            }

            buildName();
        }

        function buildName() {
            if (typeSelect.value === "Individual") {
                hiddenName.value = `${firstName.value} ${lastName.value}`.trim();
            }
        }

        firstName?.addEventListener("input", buildName);
        lastName?.addEventListener("input", buildName);
        typeSelect?.addEventListener("change", updateUI);

        updateUI();
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
                    <i class="material-icons-outlined">delete</i>
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

    // ---------- Modal Open (Server) ----------
    const viewModalEl = $('ViewClientModal');
    if (viewModalEl) {
        viewModalEl.addEventListener('show.bs.modal', async function (event) {
            closeAllCollapses();
            editingLocationIndex = null;
            editingContactIndex = null;
            setBasicMode(false);

            const btn = event.relatedTarget;
            const id = btn?.getAttribute('data-client-id');

            if (!id) {
                toastError('Missing client id.', 'Error');
                renderClient({ id: '—', type: '—', name: 'Client not found', addresses: [], contacts: [], projects: [] });
                return;
            }

            // عرض placeholder سريع داخل المودال
            renderClient({ id, type: '—', name: 'Loading...', addresses: [], contacts: [], projects: [] });

            try {
                // ✅ 1) اجلب من السيرفر
                const client = await fetchClientById(id);

                // ✅ 2) اعرض
                renderClient(client);

            } catch (err) {
                console.error('Load client failed:', err);

                // (اختياري) fallback للـ mock لو تبي
                const fallback = mockClients[id];
                if (fallback) {
                    toastInfo('Loaded from mock (server failed).', 'Info');
                    renderClient(fallback);
                    return;
                }

                toastError('Client not found or failed to load.', 'Error');
                renderClient({ id, type: '—', name: 'Client not found', addresses: [], contacts: [], projects: [] });
            }
        });
    }

    // ---------- Create Modal (FormModal) : show/hide contact section ----------
    document.addEventListener('DOMContentLoaded', function () {
        const modalEl = document.getElementById('FormModal');
        if (!modalEl) return;

        const typeSelect = modalEl.querySelector('#type');
        const contactSection = modalEl.querySelector('#contactSection');
        const modalTitle = modalEl.querySelector('.modal-title'); // optional

        if (!typeSelect || !contactSection) return;

        function updateCreateModalUI() {
            const isCompany = typeSelect.value === 'Company';
            contactSection.classList.toggle('d-none', !isCompany);
            if (modalTitle) modalTitle.textContent = isCompany ? 'Add Company' : 'Add Individual';
        }

        // ✅ NEW: clear validation when modal closes
        function clearRazorValidationState(form) {
            // jQuery validate (if موجود)
            if (window.jQuery) {
                const $form = window.jQuery(form);
                const v = $form.data('validator');
                if (v && typeof v.resetForm === 'function') v.resetForm();
            }

            // field messages
            form.querySelectorAll('[data-valmsg-for]').forEach(el => {
                el.textContent = '';
                el.classList.remove('field-validation-error');
                el.classList.add('field-validation-valid');
            });

            // summary
            form.querySelectorAll('[data-valmsg-summary="true"]').forEach(el => {
                el.innerHTML = '';
                el.classList.remove('validation-summary-errors');
                el.classList.add('validation-summary-valid');
            });

            // input error styles
            form.querySelectorAll('.input-validation-error').forEach(el => {
                el.classList.remove('input-validation-error');
                el.removeAttribute('aria-invalid');
            });
        }

        modalEl.addEventListener('hidden.bs.modal', function () {
            const form = modalEl.querySelector('form');
            if (form) clearRazorValidationState(form);
        });

        typeSelect.addEventListener('change', updateCreateModalUI);
        modalEl.addEventListener('shown.bs.modal', updateCreateModalUI);
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
            toastInfo('Export will be implemented in the next step (Lexware integration).', 'Lexware');
            return;
        }

        if (action === 'save-basic') {
            if (!currentClient) return;

            const url = document.getElementById('vcUpdateBasicUrl')?.value;
            if (!url) { toastError('Update url not found.', 'Error'); return; }

            const emailItems = collectEmailEditRows();
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

            const payload = {
                customerId: client.id,
                addressId: addressId,
                address: {
                    label: $('vc-loc-' + idx + '-label')?.value?.trim() || 'Location',
                    countryCode: $('vc-loc-' + idx + '-countryCode')?.value?.trim() || null,
                    country: $('vc-loc-' + idx + '-country')?.value?.trim() || null,
                    city: $('vc-loc-' + idx + '-city')?.value?.trim() || '',
                    postalCode: $('vc-loc-' + idx + '-postal')?.value?.trim() || '',
                    streetRaw: $('vc-loc-' + idx + '-streetRaw')?.value?.trim() || 'N/A',
                    addressLine2: $('vc-loc-' + idx + '-line2')?.value?.trim() || '',
                    fullNameOrCompany: currentClient?.name ?? '',
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
            const ok = await confirmBox('Delete this location?', 'Confirm');
            if (!ok) return;

            const url = document.getElementById('vcDeleteAddressUrl')?.value;
            if (!url) return toastError('vcDeleteAddressUrl not found', 'Error');

            const addressId = client.addresses?.[idx]?.id;
            if (!addressId) return toastError('AddressId missing.', 'Error');

            try {
                const updatedRaw = await postJson(url, { customerId: client.id, addressId });
                currentClient = normalizeClient(updatedRaw);
                renderClient(currentClient);
                toastSuccess('Location deleted (DB).', 'Success');
            } catch (err) {
                console.error(err);
                toastError('Failed to delete location.', 'Error');
            }
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
            const ok = await confirmBox('Delete this contact?', 'Confirm');
            if (!ok) return;

            const url = document.getElementById('vcDeleteContactUrl')?.value;
            if (!url) return toastError('vcDeleteContactUrl not found', 'Error');

            const contactId = client.contacts?.[idx]?.id;
            if (!contactId) return toastError('ContactId missing.', 'Error');

            try {
                const updatedRaw = await postJson(url, { customerId: client.id, contactId });
                currentClient = normalizeClient(updatedRaw);
                renderClient(currentClient);
                toastSuccess('Contact deleted (DB).', 'Success');
            } catch (err) {
                console.error(err);
                toastError('Failed to delete contact.', 'Error');
            }
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
            const payload = {
                customerId: currentClient.id,
                address: {
                    label: $('vc-add-loc-label')?.value?.trim() || 'Location',
                    countryCode: $('vc-add-loc-countryCode')?.value?.trim() || null,
                    country: $('vc-add-loc-country')?.value?.trim() || null,
                    city: $('vc-add-loc-city')?.value?.trim() || '',
                    postalCode: $('vc-add-loc-postal')?.value?.trim() || '',
                    streetRaw: $('vc-add-loc-streetRaw')?.value?.trim() || 'N/A',
                    addressLine2: $('vc-add-loc-line2')?.value?.trim() || '',
                    fullNameOrCompany: $('vc-add-loc-fullname')?.value?.trim() || currentClient.name,
                    isDefault: !!$('vc-add-loc-default')?.checked
                }

            };

            try {
                const updatedRaw = await postJson(url, payload);
                currentClient = normalizeClient(updatedRaw);
                renderClient(currentClient);

                addLocForm.reset();
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


    // ---------- Confirm for server forms (data-vc-confirm) ----------
    document.addEventListener('click', async function (e) {
        const btn = e.target.closest('[data-vc-confirm]');
        if (!btn) return;

        const type = btn.getAttribute('data-vc-confirm');
        let msg = 'Are you sure?';

        if (type === 'delete-client') msg = 'Are you sure you want to delete this client?';

        e.preventDefault();
        const ok = await confirmBox(msg, 'Confirm');
        if (!ok) return;

        btn.closest('form')?.submit();
    });

    // ---------- Table Delete (server) ----------
    document.addEventListener('click', async function (e) {
        const btn = e.target.closest('[data-vc-action="table-delete"]');
        if (!btn) return;

        e.preventDefault();

        const id = btn.getAttribute('data-client-id');

        const ok = await confirmBox('Are you sure you want to delete this client?', 'Confirm');
        if (!ok) return;

        const hid = document.getElementById('tblDeleteClientId');
        const form = document.getElementById('tblDeleteForm');
        if (!hid || !form) return;

        hid.value = id;
        form.submit();
    });


    // ---------- Server Fetch ----------
    async function fetchClientById(id) {
        const baseUrl = document.getElementById('vcClientUrl')?.value;
        if (!baseUrl) throw new Error('vcClientUrl not found');

        const joiner = baseUrl.includes('?') ? '&' : '?';
        const url = `${baseUrl}${joiner}id=${encodeURIComponent(id)}`;

        const res = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);

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



            projects: root.projects ?? root.Projects ?? []
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
                <i class="material-icons-outlined align-middle" style="font-size:18px">delete</i>
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


})();


