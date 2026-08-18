(function () {
    'use strict';

    window.UI = window.UI || {};
    const UI = window.UI;

    function $(id) { return document.getElementById(id); }

    function toastSuccess(msg, title) { UI?.toast?.success ? UI.toast.success(msg, title) : alert((title ? title + ': ' : '') + msg); }
    function toastError(msg, title) { UI?.toast?.error ? UI.toast.error(msg, title) : alert((title ? title + ': ' : '') + msg); }
    function toastInfo(msg, title) { UI?.toast?.info ? UI.toast.info(msg, title) : alert((title ? title + ': ' : '') + msg); }
    function toastWarn(msg, title) { UI?.toast?.warning ? UI.toast.warning(msg, title) : alert((title ? title + ': ' : '') + msg); }

    const RELOAD_TOAST_KEY = 'vc.toast.afterReload';

    function saveToastForReload(toastObj) {
        try {
            if (!toastObj) return;
            sessionStorage.setItem(RELOAD_TOAST_KEY, JSON.stringify(toastObj));
        } catch { }
    }

    function showToast(t) {
        if (!t) return;
        const type = (t.type || '').toString().toLowerCase();
        const title = t.title || '';
        const msg = t.message || '';

        if (type === 'success') return toastSuccess(msg, title);
        if (type === 'warning' || type === 'warn') return toastWarn(msg, title);
        if (type === 'info') return toastInfo(msg, title);
        return toastError(msg, title);
    }

    function clearErrors(prefix) {
        document.querySelectorAll(`[id^="err-${prefix}"]`).forEach(el => el.textContent = '');
    }

    function esc(s) {
        const div = document.createElement('div');
        div.textContent = s ?? '';
        return div.innerHTML;
    }

    function fmtDate(v) {
        return v ? String(v) : '—';
    }

    function fmtDateTime(v) {
        if (!v) return '—';
        try {
            const d = new Date(v);
            return d.toLocaleString();
        } catch {
            return String(v);
        }
    }

    // Statuses are worded and coloured by UI.badge, the same map the server
    // renders from, so a document reads the same here as it does in its own list.
    //
    // Each of these used to be a local ladder of if-statements ending in a
    // catch-all, and the catch-all was wrong: an unrecognised project status came
    // out as "Draft", so a cancelled or on-hold project claimed to be a draft, and
    // an invoice that had been sent did the same. Unknown statuses are now shown
    // under their own name instead of being folded into whichever branch happened
    // to be last.

    function statusBadgeHtml(st) {
        return window.UI.badge.status('project', st);
    }

    function quoteStatusBadge(st) {
        return window.UI.badge.status('quote', st);
    }

    function invoiceStatusBadge(st) {
        return window.UI.badge.status('invoice', st);
    }
    function normalizeInvoiceStatusForSelect(st) {
        const s = (st ?? '').toString().toLowerCase();

        if (s === 'paid') return 'Paid';
        if (s === 'overdue') return 'Overdue';
        if (s === 'cancelled' || s === 'void') return 'Cancelled';
        return 'Open';
    }

    function openInvoiceStatusModal(id, invoiceNo, status) {
        $('vpChangeInvoiceStatusInvoiceId').value = id || '';
        $('vpChangeInvoiceStatusInvoiceNo').textContent = invoiceNo || '—';
        $('vpChangeInvoiceStatusSelect').value = normalizeInvoiceStatusForSelect(status);

        const modalEl = $('vpChangeInvoiceStatusModal');
        if (!modalEl) return;

        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    }

    async function changeInvoiceStatus() {
        const url = $('vcChangeInvoiceStatusUrl')?.value || '';
        const token = $('antiForgeryToken')?.value || '';
        const invoiceId = $('vpChangeInvoiceStatusInvoiceId')?.value || '';
        const status = $('vpChangeInvoiceStatusSelect')?.value || '';

        if (!url) {
            toastError('Change invoice status URL not found.', 'Error');
            return;
        }

        if (!invoiceId) {
            toastError('Invoice id is missing.', 'Error');
            return;
        }

        const btn = $('vpChangeInvoiceStatusConfirmBtn');
        if (btn) btn.disabled = true;

        try {
            const body =
                `invoiceId=${encodeURIComponent(invoiceId)}` +
                `&status=${encodeURIComponent(status)}`;

            const res = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                    'Accept': 'application/json',
                    ...(token ? { 'RequestVerificationToken': token } : {})
                },
                body
            });

            const data = await res.json().catch(() => null);

            if (!res.ok || !data?.ok) {
                showToast(data?.toast || { type: 'error', title: 'Error', message: 'Failed to change invoice status.' });
                return;
            }

            showToast(data.toast || { type: 'success', title: 'Done', message: 'Invoice status changed successfully.' });

            const modalEl = $('vpChangeInvoiceStatusModal');
            if (modalEl) {
                bootstrap.Modal.getOrCreateInstance(modalEl).hide();
            }

            await loadInvoices({ page: invoicesState.page, q: invoicesState.q || '' });
        } catch (err) {
            console.error(err);
            toastError('Failed to change invoice status.', 'Error');
        } finally {
            if (btn) btn.disabled = false;
        }
    }
    // The contract panel's status. Only ever a contract, so it reads from the
    // contract vocabulary — where a sent contract is "Awaiting signature", which
    // says who is now being waited on, rather than the bare "Sent" this used to
    // show.
    function badgeHtml(status) {
        return window.UI.badge.status('contract', status);
    }

    function toastFromPayload(payload, fallbackTitle, fallbackMsg) {
        if (payload?.toast) { showToast(payload.toast); return; }
        if (payload?.message) { toastError(payload.message, fallbackTitle || 'Error'); return; }
        toastError(fallbackMsg || 'Request failed.', fallbackTitle || 'Error');
    }

    async function postJson(url, body) {
        const token = $('antiForgeryToken')?.value || '';

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
            try { payload = isJson ? await res.json() : { message: await res.text() }; }
            catch { payload = { message: 'Request failed.' }; }

            throw { status: res.status, payload };
        }

        return isJson ? await res.json() : null;
    }

    async function fetchProjectById(id) {
        const baseUrl = $('vcProjectUrl')?.value;
        if (!baseUrl) throw new Error('vcProjectUrl not found');

        const joiner = baseUrl.includes('?') ? '&' : '?';
        const url = `${baseUrl}${joiner}id=${encodeURIComponent(id)}`;

        const res = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } });

        const contentType = res.headers.get('content-type') || '';
        const isJson = contentType.includes('application/json');

        if (!res.ok) {
            let payload = null;
            try { payload = isJson ? await res.json() : { message: await res.text() }; }
            catch { payload = { message: 'Request failed.' }; }

            if (payload?.toast) showToast(payload.toast);
            else toastError(payload?.message || `HTTP ${res.status}`, 'Error');

            throw new Error(`HTTP ${res.status}`);
        }

        return res.json();
    }

    function normalizeProject(raw) {
        const r = raw || {};
        const customer = r.customer ?? r.Customer ?? {};

        return {
            id: r.id ?? r.Id,
            title: r.title ?? r.Title,
            description: r.description ?? r.Description,
            status: r.status ?? r.Status,
            startDate: r.startDate ?? r.StartDate,
            endDate: r.endDate ?? r.EndDate,
            customer: {
                id: customer.id ?? customer.Id,
                name: customer.name ?? customer.Name,
                email: customer.email ?? customer.Email
            }
        };
    }

    let currentProject = null;
    let editingBasic = false;

    const projectId = ($('wsProjectId')?.value || '').trim();
    function isValidWorkspaceTab(tab) {
        return tab === 'overview' || tab === 'quotes' || tab === 'invoices' || tab === 'contracts';
    }

    function getWorkspaceTabStorageKey() {
        return `wh.project.workspace.tab:${projectId}`;
    }

    function getTabNameFromButton(btn) {
        const target = btn?.getAttribute('data-bs-target') || '';
        const match = target.match(/^#vp-pane-(overview|quotes|invoices|contracts)$/);
        return match ? match[1] : 'overview';
    }

    function updateWorkspaceTabState(tab) {
        if (!projectId || !isValidWorkspaceTab(tab)) return;

        try {
            sessionStorage.setItem(getWorkspaceTabStorageKey(), tab);
        } catch { }

        const url = new URL(window.location.href);
        url.searchParams.set('id', projectId);
        url.searchParams.set('tab', tab);
        window.history.replaceState({}, document.title, url.pathname + url.search);
    }

    function getInitialWorkspaceTab() {
        const queryTab = (new URLSearchParams(window.location.search).get('tab') || '').trim().toLowerCase();
        if (isValidWorkspaceTab(queryTab)) return queryTab;

        try {
            const savedTab = (sessionStorage.getItem(getWorkspaceTabStorageKey()) || '').trim().toLowerCase();
            if (isValidWorkspaceTab(savedTab)) return savedTab;
        } catch { }

        const hiddenTab = (($('wsOpenTab')?.value || '').trim().toLowerCase());
        if (isValidWorkspaceTab(hiddenTab)) return hiddenTab;

        return 'overview';
    }
    const quotesState = {
        projectId,
        page: 1,
        pageSize: 10,
        q: null,
        loadedOnce: false
    };

    const invoicesState = {
        projectId,
        page: 1,
        pageSize: 10,
        q: null,
        loadedOnce: false
    };

    const contractState = {
        projectId,
        loadedOnce: false
    };

    function setBasicMode(isEdit) {
        editingBasic = !!isEdit;
        clearErrors('vp-e');

        $('vp-basicView')?.classList.toggle('d-none', editingBasic);
        $('vp-basicEdit')?.classList.toggle('d-none', !editingBasic);
    }

    /// Writes text into an element only if the page actually has it.
    ///
    /// Every write here used to be unguarded, so removing one element from the
    /// markup threw a TypeError in the middle of rendering — and because the render
    /// runs inside the load's try block, the page reported "Failed to load
    /// project." for a project that had loaded perfectly well. The panel then never
    /// appeared, which made it look as though projects and contracts could not be
    /// opened at all.
    function setText(id, value) {
        const el = $(id);
        if (el) el.textContent = value;
    }

    function setValue(id, value) {
        const el = $(id);
        if (el) el.value = value;
    }

    function renderProject(p) {
        currentProject = p;

        // The project's status and identity are rendered on the server now, in the
        // page header, so they are deliberately not written here: two sources for
        // one fact is what let them disagree.
        setText('vp-title', p.title ?? 'Project');

        const customerNameEl = $('vp-customerName');
        const customerEmailEl = $('vp-customerEmail');

        if (customerNameEl) {
            customerNameEl.textContent = p.customer?.name ?? '—';

            if (p.customer?.id) {
                customerNameEl.href = `/Clients/Details/${encodeURIComponent(p.customer.id)}`;
                customerNameEl.classList.remove('text-muted', 'pe-none');
            } else {
                customerNameEl.href = '#';
                customerNameEl.classList.add('text-muted', 'pe-none');
            }
        }

        if (customerEmailEl) {
            customerEmailEl.textContent = p.customer?.email ?? '—';
        }

        setText('vp-v-title', p.title ?? '—');
        setText('vp-v-desc', p.description ?? '—');
        setText('vp-v-dates', `${fmtDate(p.startDate)} → ${fmtDate(p.endDate)}`);

        setValue('vp-e-title', p.title ?? '');
        setValue('vp-e-desc', p.description ?? '');
        setValue('vp-e-start', p.startDate ?? '');
        setValue('vp-e-end', p.endDate ?? '');

        const q = $('vp-newQuoteBtn');
        if (q && p.id) q.href = `/Quotes/Create?projectId=${encodeURIComponent(p.id)}`;

        setBasicMode(false);
    }

    function activateInitialTab() {
        const tab = getInitialWorkspaceTab();
        const btn =
            document.querySelector(`#vpTabs button[data-bs-target="#vp-pane-${tab}"]`) ||
            $('vp-tab-overview');

        if (!btn) return;

        bootstrap.Tab.getOrCreateInstance(btn).show();
        updateWorkspaceTabState(tab);
    }

    async function loadProject() {
        if (!projectId) {
            $('vpLoading').textContent = 'Project id is missing.';
            return;
        }

        $('vpLoading')?.classList.remove('d-none');
        $('vpBody')?.classList.add('d-none');

        // Loading and rendering are reported separately. They used to share one
        // catch, so a fault in the rendering — an element the markup no longer had
        // — was reported as "Failed to load project." for a project that had loaded
        // perfectly well, and the message sent everyone looking in the wrong place.
        let data;

        try {
            data = await fetchProjectById(projectId);
        } catch (err) {
            console.error('Loading the project failed.', err);

            const el = $('vpLoading');
            if (el) el.textContent = 'This project could not be loaded. Reload the page, or open it again from the projects list.';

            return;
        }

        try {
            renderProject(normalizeProject(data));

            $('vpLoading')?.classList.add('d-none');
            $('vpBody')?.classList.remove('d-none');

            activateInitialTab();
        } catch (err) {
            console.error('The project loaded but could not be displayed.', err);

            const el = $('vpLoading');
            if (el) el.textContent = 'This project loaded but could not be displayed. Reload the page — if it keeps happening, the details are in the browser console.';
        }
    }

    function setQuotesLoading(on) {
        $('vpQuotesLoading')?.classList.toggle('d-none', !on);
    }

    function setQuotesEmpty(on) {
        $('vpQuotesEmpty')?.classList.toggle('d-none', !on);
    }

    function setQuotesTableVisible(on) {
        $('vpQuotesTable')?.classList.toggle('d-none', !on);
    }

    function buildQuotesPager(res) {
        const ul = $('vpQuotesPager');
        const wrap = $('vpQuotesPagerWrap');
        if (!ul || !wrap) return;

        ul.innerHTML = '';

        const page = Number(res.page ?? res.Page ?? 1);
        const totalPages =
            Number(res.totalPages ?? res.TotalPages ?? 0) ||
            Math.max(1, Math.ceil(Number(res.totalItems ?? res.TotalItems ?? 0) / Number(res.pageSize ?? res.PageSize ?? 10)));

        wrap.classList.toggle('d-none', totalPages <= 1);

        function addItem(label, targetPage, disabled, active) {
            const li = document.createElement('li');
            li.className = `page-item ${disabled ? 'disabled' : ''} ${active ? 'active' : ''}`;

            const a = document.createElement('a');
            a.className = 'page-link';
            a.href = '#';
            a.textContent = label;

            if (!disabled && !active) {
                a.addEventListener('click', function (e) {
                    e.preventDefault();
                    loadQuotes({ page: targetPage });
                });
            }

            li.appendChild(a);
            ul.appendChild(li);
        }

        addItem('Prev', page - 1, page <= 1, false);

        const start = Math.max(1, page - 2);
        const end = Math.min(totalPages, page + 2);

        for (let p = start; p <= end; p++) addItem(String(p), p, false, p === page);

        addItem('Next', page + 1, page >= totalPages, false);
    }

    async function loadQuotes(opts) {
        if (!quotesState.projectId) return;

        const urlBase = $('vcProjectQuotesUrl')?.value;
        if (!urlBase) return toastError('vcProjectQuotesUrl not found', 'Error');

        if (opts?.page) quotesState.page = opts.page;
        if (opts?.q !== undefined) quotesState.q = opts.q;

        setQuotesLoading(true);
        setQuotesEmpty(false);
        setQuotesTableVisible(false);
        $('vpQuotesPagerWrap')?.classList.add('d-none');

        const joiner = urlBase.includes('?') ? '&' : '?';
        const url =
            `${urlBase}${joiner}` +
            `projectId=${encodeURIComponent(quotesState.projectId)}` +
            `&p=${encodeURIComponent(quotesState.page)}` +
            `&pageSize=${encodeURIComponent(quotesState.pageSize)}` +
            `&q=${encodeURIComponent(quotesState.q || '')}`;

        try {
            const res = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } });
            const json = await res.json().catch(() => ({}));

            if (!res.ok) {
                toastFromPayload(json, 'Error', 'Failed to load quotes.');
                return;
            }

            const data = json.data ?? json;
            const items = data.items ?? data.Items ?? [];

            const tbody = $('vpQuotesTbody');
            if (!tbody) return;

            tbody.innerHTML = '';

            if (!items.length) {
                setQuotesLoading(false);
                setQuotesEmpty(true);
                setQuotesTableVisible(false);
                quotesState.loadedOnce = true;
                return;
            }

            for (const q of items) {
                const id = q.id ?? q.Id;
                const quoteNo = q.quoteNo ?? q.QuoteNo ?? '—';
                const status = q.status ?? q.Status;
                const createdAt = q.createdAt ?? q.CreatedAt;
                const total = q.itemsTotal ?? q.ItemsTotal ?? 0;

                const tr = document.createElement('tr');
                tr.innerHTML = `
                    <td>${esc(quoteNo)}</td>
                    <td>${quoteStatusBadge(status)}</td>
                    <td>${esc(fmtDate(createdAt))}</td>
                    <td class="text-end">${Number(total || 0).toFixed(2)}</td>
                    <td class="text-end">
                        <a class="btn btn-sm btn-outline-primary" href="/Quotes/Details?id=${encodeURIComponent(id)}">Details</a>
                    </td>
                `;
                tbody.appendChild(tr);
            }

            setQuotesLoading(false);
            setQuotesEmpty(false);
            setQuotesTableVisible(true);
            buildQuotesPager(data);
            quotesState.loadedOnce = true;
        } catch (err) {
            console.error(err);
            setQuotesLoading(false);
            toastError('Failed to load quotes.', 'Error');
        }
    }

    function setInvoicesLoading(on) {
        $('vpInvoicesLoading')?.classList.toggle('d-none', !on);
    }

    function setInvoicesEmpty(on) {
        $('vpInvoicesEmpty')?.classList.toggle('d-none', !on);
    }

    function setInvoicesVisible(on) {
        $('vpInvoicesTable')?.classList.toggle('d-none', !on);
    }

    function buildInvoicesPager(res) {
        const ul = $('vpInvoicesPager');
        const wrap = $('vpInvoicesPagerWrap');
        if (!ul || !wrap) return;

        ul.innerHTML = '';

        const page = Number(res.page ?? res.Page ?? 1);
        const totalPages =
            Number(res.totalPages ?? res.TotalPages ?? 0) ||
            Math.max(1, Math.ceil(Number(res.totalItems ?? res.TotalItems ?? 0) / Number(res.pageSize ?? res.PageSize ?? 10)));

        wrap.classList.toggle('d-none', totalPages <= 1);

        function addItem(label, targetPage, disabled, active) {
            const li = document.createElement('li');
            li.className = `page-item ${disabled ? 'disabled' : ''} ${active ? 'active' : ''}`;

            const a = document.createElement('a');
            a.className = 'page-link';
            a.href = '#';
            a.textContent = label;

            if (!disabled && !active) {
                a.addEventListener('click', function (e) {
                    e.preventDefault();
                    loadInvoices({ page: targetPage });
                });
            }

            li.appendChild(a);
            ul.appendChild(li);
        }

        addItem('Prev', page - 1, page <= 1, false);

        const start = Math.max(1, page - 2);
        const end = Math.min(totalPages, page + 2);

        for (let p = start; p <= end; p++) addItem(String(p), p, false, p === page);

        addItem('Next', page + 1, page >= totalPages, false);
    }

    async function loadInvoices(opts) {
        if (!invoicesState.projectId) return;

        const urlBase = $('vcProjectInvoicesUrl')?.value;
        if (!urlBase) return toastError('vcProjectInvoicesUrl not found', 'Error');

        if (opts?.page) invoicesState.page = opts.page;
        if (opts?.q !== undefined) invoicesState.q = opts.q;

        setInvoicesLoading(true);
        setInvoicesEmpty(false);
        setInvoicesVisible(false);
        $('vpInvoicesPagerWrap')?.classList.add('d-none');

        const joiner = urlBase.includes('?') ? '&' : '?';
        const url =
            `${urlBase}${joiner}` +
            `projectId=${encodeURIComponent(invoicesState.projectId)}` +
            `&p=${encodeURIComponent(invoicesState.page)}` +
            `&pageSize=${encodeURIComponent(invoicesState.pageSize)}` +
            `&q=${encodeURIComponent(invoicesState.q || '')}`;

        try {
            const res = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } });
            const json = await res.json().catch(() => ({}));

            if (!res.ok) {
                toastFromPayload(json, 'Error', 'Failed to load invoices.');
                return;
            }

            const root = json ?? {};
            const data = root.data ?? root;
            const list =
                (data && (data.items || data.Items)) ? data :
                    (data.data ?? data.Data ?? data.result ?? data.Result ?? data);

            const itemsRaw = list.items ?? list.Items ?? [];
            const items = Array.isArray(itemsRaw) ? itemsRaw : [];

            const tbody = $('vpInvoicesTbody');
            if (!tbody) {
                setInvoicesLoading(false);
                toastError('Invoices table not found.', 'Error');
                return;
            }

            tbody.innerHTML = '';

            if (!items.length) {
                setInvoicesLoading(false);
                setInvoicesEmpty(true);
                setInvoicesVisible(false);
                invoicesState.loadedOnce = true;
                return;
            }

            for (const inv of items) {
                const id = inv.id ?? inv.Id;
                const invoiceNo = inv.invoiceNo ?? inv.InvoiceNo ?? '—';
                const status = inv.status ?? inv.Status;
                const createdAt = inv.createdAt ?? inv.CreatedAt;
                const issueDate = inv.issueDate ?? inv.IssueDate;
                const dueDate = inv.dueDate ?? inv.DueDate;
                const total = inv.total ?? inv.Total ?? inv.itemsTotal ?? inv.ItemsTotal ?? 0;

                const totalNum = Number(total);
                const totalText = Number.isFinite(totalNum) ? totalNum.toFixed(2) : '0.00';

                const pdfBaseUrl = $('vcInvoicePdfViewerUrl')?.value || '';
                const joinerPdf = pdfBaseUrl.includes('?') ? '&' : '?';
                const pdfUrl = `${pdfBaseUrl}${joinerPdf}id=${encodeURIComponent(id)}`;

                const tr = document.createElement('tr');
                tr.className = 'wh-clickable-row';
                tr.innerHTML = `
                    <td>${esc(invoiceNo)}</td>
                    <td>${invoiceStatusBadge(status)}</td>
                    <td>${esc(fmtDate(createdAt))}</td>
                    <td>${esc(fmtDate(issueDate))}</td>
                    <td>${esc(fmtDate(dueDate))}</td>
                    <td class="text-end">${totalText}</td>
                  <td class="text-end">
    <button type="button"
            class="btn text-warning wh-icon-btn-plain js-change-invoice-status"
            data-id="${esc(id)}"
            data-invoice-no="${esc(invoiceNo)}"
            data-status="${esc(status ?? '')}"
            title="Change Status"
            aria-label="Change Status">
        <i class="ri-edit-line"></i>
    </button>

    <a class="btn text-primary wh-icon-btn-plain"
       href="${pdfUrl}"
       target="_blank"
       rel="noopener"
       title="Details"
       aria-label="Details">
        <i class="ri-file-list-3-line"></i>
    </a>
</td>
                `;

                tr.addEventListener('click', function (e) {
                    if (e.target.closest('a,button,input,textarea,select,label')) return;
                    window.open(pdfUrl, '_blank', 'noopener');
                });

                tbody.appendChild(tr);
            }

            setInvoicesLoading(false);
            setInvoicesEmpty(false);
            setInvoicesVisible(true);
            buildInvoicesPager(list);
            invoicesState.loadedOnce = true;
        } catch (err) {
            console.error(err);
            setInvoicesLoading(false);
            toastError('Failed to load invoices.', 'Error');
        }
    }
    function setContractLoading(on) {
        $('vpContractLoading')?.classList.toggle('d-none', !on);
    }

    function showContractEmpty(on) {
        $('vpContractEmpty')?.classList.toggle('d-none', !on);
    }

    function showContractPreview(on) {
        $('vpContractPreviewWrap')?.classList.toggle('d-none', !on);
    }

    async function loadProjectContractSnapshot() {
        if (!contractState.projectId) return;

        const urlBase = $('vcProjectContractSnapshotUrl')?.value;
        if (!urlBase) return toastError('vcProjectContractSnapshotUrl not found', 'Error');

        const btnCreate = $('vpContractCreateBtn');
        
        const editLink = $('vpContractEditLink');
        const detailsLink = $('vpContractDetailsLink');
        const sendBtn = $('vpContractSendBtn');
        const itemsLink = $('vpContractItemsLink');
        const copyLinkBtn = $('vpContractCopyLinkBtn');

        setContractLoading(true);
        showContractEmpty(false);
        showContractPreview(false);

        const joiner = urlBase.includes('?') ? '&' : '?';
        const url = `${urlBase}${joiner}projectId=${encodeURIComponent(contractState.projectId)}`;

        try {
            const res = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } });
            const json = await res.json().catch(() => ({}));

            if (!res.ok) {
                toastFromPayload(json, 'Error', 'Failed to load contract.');
                return;
            }

            const data = json.data ?? json;

            if (!data.exists) {
                $('vpContractTitle').textContent = 'No contract';
                $('vpContractMeta').textContent = '—';
                $('vpContractStatusBadge').innerHTML = '';
                $('vpContractPreview').innerHTML = '';

                btnCreate?.classList.remove('d-none');
                btnCreate.textContent = 'Add Contract';

                editLink?.classList.add('d-none');
                detailsLink?.classList.add('d-none');
                sendBtn?.classList.add('d-none');
                copyLinkBtn?.classList.add('d-none');
                itemsLink?.classList.add('d-none');

                showContractEmpty(true);
                showContractPreview(false);

                contractState.loadedOnce = true;
                return;
            }

            const status = (data.status || '').toString().toLowerCase();
            const isSigned = status === 'signed' || !!data.signedAt;
            const canUpdate = !!data.canUpdate && !isSigned;

            $('vpContractTitle').textContent = data.contractNo || 'Contract';
            // $('vpContractMeta').textContent = data.signedAt ? `Signed at: ${fmtDateTime(data.signedAt)}` : 'Not signed yet';
            const contractStatusText = (data.status || '').toString().toLowerCase();

            $('vpContractMeta').textContent =
                contractStatusText === 'signed' && data.signedAt
                    ? `Signed at: ${fmtDateTime(data.signedAt)}`
                    : contractStatusText === 'sent'
                        ? 'Sent'
                        : contractStatusText === 'draft'
                            ? 'Draft'
                            : data.signedAt
                                ? `Signed at: ${fmtDateTime(data.signedAt)}`
                                : (data.status || 'Draft');
            $('vpContractStatusBadge').innerHTML = badgeHtml(data.status);

            const cid = data.contractId || data.id || null;
            const itemsCount = Number(data.itemsCount || 0);
            const hasItems = itemsCount > 0;
            const hasTerms = !!data.hasTerms;

            if (itemsLink && cid && canUpdate) {
                itemsLink.href = `/Contracts/Items/Manage?contractId=${encodeURIComponent(cid)}&returnTo=items`;
                itemsLink.textContent = 'Positions';
                itemsLink.classList.remove('d-none');
            } else {
                itemsLink?.classList.add('d-none');
            }

            $('vpContractPreview').innerHTML = data.previewHtml || '<div class="text-muted">No contract terms yet.</div>';

            if (!hasItems) {
                if (itemsLink && cid && canUpdate) {
                    itemsLink.href = `/Contracts/Items/Manage?contractId=${encodeURIComponent(cid)}&returnTo=items&toast=noItems`;
                    itemsLink.textContent = 'Add Positions';
                    itemsLink.classList.remove('d-none');
                } else {
                    itemsLink?.classList.add('d-none');
                }

                btnCreate?.classList.add('d-none');
                editLink?.classList.add('d-none');
                detailsLink?.classList.add('d-none');
                sendBtn?.classList.add('d-none');
                btnCreate?.classList.add('d-none');

                $('vpContractPreview').innerHTML =
                    '<div class="alert alert-warning mb-0">This contract has no Positions. Please add at least one line item to continue.</div>';

                showContractEmpty(false);
                showContractPreview(true);
                contractState.loadedOnce = true;
                return;
            }

            btnCreate?.classList.add('d-none');

            

            if (editLink) {
                editLink.href = data.editUrl || '#';
                editLink.classList.toggle('d-none', !canUpdate);
            }

            if (detailsLink) {
                let href = data.detailsUrl || '';
                if (!href && cid) href = `/Contracts/Details?id=${encodeURIComponent(cid)}`;
                detailsLink.href = href || '#';
                detailsLink.classList.toggle('d-none', isSigned || !hasTerms);
            }

            if (sendBtn) {
                sendBtn.classList.remove('d-none');

                if (isSigned) {
                    sendBtn.disabled = true;
                    sendBtn.textContent = 'Signed';
                    sendBtn.classList.remove('btn-outline-success');
                    sendBtn.classList.add('btn-success');
                    sendBtn.title = 'This contract is already signed.';
                } else {
                    sendBtn.disabled = !hasItems;
                    sendBtn.textContent = 'Send';
                    sendBtn.classList.remove('btn-success');
                    sendBtn.classList.add('btn-outline-success');
                    sendBtn.removeAttribute('title');
                }
            }

            if (copyLinkBtn) {
                if (isSigned) {
                    copyLinkBtn.classList.add('d-none');
                } else {
                    copyLinkBtn.classList.remove('d-none');
                    copyLinkBtn.disabled = !hasItems;
                    copyLinkBtn.textContent = 'Copy Link';
                    copyLinkBtn.removeAttribute('title');
                }
            }

            showContractEmpty(false);
            showContractPreview(true);
            contractState.loadedOnce = true;
        } catch (err) {
            console.error(err);
            toastError('Failed to load contract.', 'Error');
        } finally {
            setContractLoading(false);
        }
    }

    async function createProjectContract() {
        if (!contractState.projectId) return;

        const urlBase = $('vcProjectContractCreateUrl')?.value;
        if (!urlBase) return toastError('Create endpoint is missing.', 'Error');

        const btn = $('vpContractCreateBtn');

        try {
            btn.disabled = true;
            UI?.loading?.show?.('Preparing Positions...');

            const token = $('antiForgeryToken')?.value || '';
            const joiner = urlBase.includes('?') ? '&' : '?';
            const url = `${urlBase}${joiner}projectId=${encodeURIComponent(contractState.projectId)}`;

            const res = await fetch(url, {
                method: 'POST',
                headers: {
                    ...(token ? { 'RequestVerificationToken': token } : {}),
                    'Accept': 'application/json'
                }
            });

            const json = await res.json().catch(() => ({}));
            const data = json?.data || {};

            if (data.redirectUrl) {
                const u = new URL(data.redirectUrl, window.location.origin);
                u.searchParams.set('toast', 'noItems');

                try { sessionStorage.removeItem(RELOAD_TOAST_KEY); } catch { }

                window.location.href = u.pathname + u.search;
                return;
            }

            if (!res.ok) {
                if (json?.toast) showToast(json.toast);
                else toastError('Failed to create contract.', 'Error');
                return;
            }

            if (json?.ok !== true) {
                if (json?.toast) showToast(json.toast);
                else showToast({ type: 'warning', title: 'Warning', message: 'Action not allowed.' });
                return;
            }

            if (json?.toast) showToast(json.toast);

            if (data.detailsUrl) {
                window.location.href = data.detailsUrl;
                return;
            }

            await loadProjectContractSnapshot();
        } catch (err) {
            console.error(err);
            toastError('Failed to create contract.', 'Error');
        } finally {
            UI?.loading?.hide?.();
            if (btn) btn.disabled = false;
        }
    }

    const copyLinkBtn = $('vpContractCopyLinkBtn');
    const confirmModalEl = $('vpSendContractModal');
    const confirmBtn = $('vpSendContractConfirmBtn');
    const cancelBtn = $('vpSendContractCancelBtn');
    const spinner = $('vpSendContractSpinner');
    const btnText = $('vpSendContractBtnText');

    function setSending(isSending) {
        if (confirmBtn) confirmBtn.disabled = isSending;
        if (cancelBtn) cancelBtn.disabled = isSending;
        if (spinner) spinner.classList.toggle('d-none', !isSending);
        if (btnText) btnText.textContent = isSending ? 'Sending...' : 'Confirm & Send';
    }

    function openSendConfirm() {
        const sendBtn = $('vpContractSendBtn');
        if (!sendBtn || sendBtn.disabled) return;

        $('vpSendContractProject').textContent = ($('vp-title')?.textContent || '—').trim();
        $('vpSendContractContract').textContent = ($('vpContractTitle')?.textContent || '—').trim();
        $('vpSendContractCustomerName').textContent = ($('vp-customerName')?.textContent || '—').trim();

        const email = ($('vp-customerEmail')?.textContent || '').trim();
        $('vpSendContractCustomerEmail').textContent = (email && email !== '—') ? email : '';

        setSending(false);
        bootstrap.Modal.getOrCreateInstance(confirmModalEl).show();
    }
    async function copyProjectContractLink() {
        if (!contractState.projectId) {
            showToast({ type: 'error', title: 'Error', message: 'ProjectId is missing.' });
            return;
        }

        const url = ($('vcProjectContractCopyLinkUrl')?.value || '').trim();
        const token = ($('antiForgeryToken')?.value || '').trim();

        if (!url) {
            showToast({ type: 'error', title: 'Error', message: 'Copy link URL not found.' });
            return;
        }

        const btn = $('vpContractCopyLinkBtn');
        if (btn?.dataset.busy === '1') return;

        try {
            if (btn) btn.dataset.busy = '1';

            const body = `projectId=${encodeURIComponent(contractState.projectId)}`;

            const res = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                    ...(token ? { 'RequestVerificationToken': token } : {})
                },
                body
            });

            const data = await res.json().catch(() => null);

            if (!res.ok || !data?.ok || !data?.data?.url) {
                showToast(data?.toast || { type: 'error', title: 'Error', message: 'Failed to create contract link.' });
                return;
            }

            await navigator.clipboard.writeText(data.data.url);

            showToast({
                type: 'success',
                title: 'Copied',
                message: 'Contract link copied.'
            });
        } catch (err) {
            console.error(err);
            showToast({ type: 'error', title: 'Server error', message: 'Failed to copy contract link.' });
        } finally {
            if (btn) delete btn.dataset.busy;
        }
    }

    async function sendProjectContract() {
        if (!contractState.projectId) {
            showToast({ type: 'error', title: 'Error', message: 'ProjectId is missing.' });
            return;
        }

        const url = ($('vcProjectContractSendUrl')?.value || '').trim();
        const token = ($('antiForgeryToken')?.value || '').trim();

        if (!url) {
            showToast({ type: 'error', title: 'Error', message: 'Send URL not found.' });
            return;
        }

        const sendBtn = $('vpContractSendBtn');
        const prevSendDisabled = sendBtn ? sendBtn.disabled : null;

        if (sendBtn) sendBtn.disabled = true;
        setSending(true);

        try {
            const body = `projectId=${encodeURIComponent(contractState.projectId)}`;

            const res = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                    ...(token ? { 'RequestVerificationToken': token } : {})
                },
                body
            });

            const data = await res.json().catch(() => null);

            if (!res.ok || !data?.ok) {
                showToast(data?.toast || { type: 'error', title: 'Error', message: 'Failed to send contract.' });
                setSending(false);
                if (sendBtn && prevSendDisabled !== null) sendBtn.disabled = prevSendDisabled;
                return;
            }

            showToast(data.toast || { type: 'success', title: 'Sent', message: 'Contract email sent successfully.' });

            bootstrap.Modal.getOrCreateInstance(confirmModalEl).hide();
            if (sendBtn && prevSendDisabled !== null) sendBtn.disabled = prevSendDisabled;

            await loadProjectContractSnapshot();
        } catch (err) {
            console.error(err);
            showToast({ type: 'error', title: 'Server error', message: 'An unexpected error occurred.' });
            setSending(false);
            if (sendBtn && prevSendDisabled !== null) sendBtn.disabled = prevSendDisabled;
        }
    }

    async function saveBasic() {
        if (!currentProject?.id) return;

        const url = $('vcUpdateProjectUrl')?.value;
        if (!url) return toastError('vcUpdateProjectUrl not found', 'Error');

        clearErrors('vp-e');

        const payload = {
            projectId: currentProject.id,
            project: {
                title: $('vp-e-title')?.value?.trim() ?? '',
                description: $('vp-e-desc')?.value ?? null,
                startDate: $('vp-e-start')?.value || null,
                endDate: $('vp-e-end')?.value || null
            }
        };

        try {
            const raw = await postJson(url, payload);
            const data = raw?.data ?? raw;

            renderProject(normalizeProject(data));

            if (raw?.toast) showToast(raw.toast);
            else showToast({ type: 'success', title: 'Success', message: 'Saved successfully.' });
        } catch (err) {
            console.error(err);

            if (err?.status === 400 && err?.payload?.errors) {
                (err.payload.errors || []).forEach(x => {
                    const f = (x.field || '').toLowerCase();
                    if (f.includes('title')) $('err-vp-e-title').textContent = x.error;
                    if (f.includes('description')) $('err-vp-e-desc').textContent = x.error;
                    if (f.includes('start')) $('err-vp-e-start').textContent = x.error;
                    if (f.includes('end')) $('err-vp-e-end').textContent = x.error;
                });

                toastError('Please fix the highlighted fields.', 'Validation');
                return;
            }

            toastFromPayload(err?.payload, 'Error', 'Failed to save.');
        }
    }

    document.addEventListener('shown.bs.tab', function (e) {
        const target = e.target;
        const tabName = getTabNameFromButton(target);

        updateWorkspaceTabState(tabName);

        if (target?.id === 'vp-tab-quotes' && !quotesState.loadedOnce) {
            loadQuotes({ page: 1, q: $('vp-quotes-q')?.value?.trim() || '' });
        }

        if (target?.id === 'vp-tab-invoices' && !invoicesState.loadedOnce) {
            loadInvoices({ page: 1, q: $('vp-invoices-q')?.value?.trim() || '' });
        }

        if (target?.id === 'vp-tab-contracts') {
            loadProjectContractSnapshot();
        }
    });
    document.addEventListener('click', function (e) {
        const btn = e.target.closest('.js-change-invoice-status');
        if (!btn) return;

        e.preventDefault();
        e.stopPropagation();

        openInvoiceStatusModal(
            btn.getAttribute('data-id'),
            btn.getAttribute('data-invoice-no'),
            btn.getAttribute('data-status')
        );
    });
    $('vp-quotes-searchBtn')?.addEventListener('click', function () {
        loadQuotes({ page: 1, q: $('vp-quotes-q')?.value?.trim() || '' });
    });

    $('vp-quotes-q')?.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            loadQuotes({ page: 1, q: $('vp-quotes-q')?.value?.trim() || '' });
        }
    });

    $('vp-invoices-searchBtn')?.addEventListener('click', function () {
        loadInvoices({ page: 1, q: $('vp-invoices-q')?.value?.trim() || '' });
    });

    $('vp-invoices-q')?.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            loadInvoices({ page: 1, q: $('vp-invoices-q')?.value?.trim() || '' });
        }
    });

    $('vpEditBasicBtn')?.addEventListener('click', function () {
        setBasicMode(true);
    });

    $('vpCancelBasicBtn')?.addEventListener('click', function () {
        if (currentProject) renderProject(currentProject);
        setBasicMode(false);
    });

    $('vpSaveBasicBtn')?.addEventListener('click', saveBasic);
    $('vpContractCreateBtn')?.addEventListener('click', createProjectContract);
    $('vpContractSendBtn')?.addEventListener('click', openSendConfirm);
    $('vpContractCopyLinkBtn')?.addEventListener('click', copyProjectContractLink);
    $('vpSendContractConfirmBtn')?.addEventListener('click', sendProjectContract);
    $('vpChangeInvoiceStatusConfirmBtn')?.addEventListener('click', changeInvoiceStatus);
    (function bindDatePickers() {
        function bindOne(el) {
            if (!el || el.dataset.dpBound === '1') return;
            el.dataset.dpBound = '1';

            let openOnFocus = false;

            el.addEventListener('pointerdown', () => { openOnFocus = true; });

            el.addEventListener('focus', () => {
                if (!openOnFocus) return;
                openOnFocus = false;
                try { el.showPicker?.(); } catch { }
            });

            el.addEventListener('click', () => {
                try { el.showPicker?.(); } catch { }
            });

            el.addEventListener('keydown', () => { openOnFocus = false; });
        }

        document.querySelectorAll('input[type="date"].js-datepicker').forEach(bindOne);
    })();

    document.addEventListener('DOMContentLoaded', loadProject);

})();
