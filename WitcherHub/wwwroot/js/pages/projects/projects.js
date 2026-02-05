(function () {
    'use strict';
    window.UI = window.UI || {};
    const UI = window.UI;

    function $(id) { return document.getElementById(id); }

    function toastSuccess(msg, title) { UI?.toast?.success ? UI.toast.success(msg, title) : alert((title ? title + ": " : "") + msg); }
    function toastError(msg, title) { UI?.toast?.error ? UI.toast.error(msg, title) : alert((title ? title + ": " : "") + msg); }
    function toastInfo(msg, title) { UI?.toast?.info ? UI.toast.info(msg, title) : alert((title ? title + ": " : "") + msg); }
    function toastWarn(msg, title) { UI?.toast?.warning ? UI.toast.warning(msg, title) : alert((title ? title + ": " : "") + msg); }

    // -------------------------
    // ✅ sessionStorage Toast (after reload)
    // -------------------------
    const RELOAD_TOAST_KEY = 'vc.toast.afterReload';

    function saveToastForReload(toastObj) {
        try {
            if (!toastObj) return;
            sessionStorage.setItem(RELOAD_TOAST_KEY, JSON.stringify(toastObj));
        } catch { /* ignore */ }
    }

    function popToastAfterReload() {
        try {
            const raw = sessionStorage.getItem(RELOAD_TOAST_KEY);
            if (!raw) return null;
            sessionStorage.removeItem(RELOAD_TOAST_KEY);
            return JSON.parse(raw);
        } catch {
            try { sessionStorage.removeItem(RELOAD_TOAST_KEY); } catch { }
            return null;
        }
    }

    function showToast(t) {
        if (!t) return;
        const type = (t.type || '').toString().toLowerCase();
        const title = t.title || '';
        const msg = t.message || '';

        if (type === 'success') return toastSuccess(msg, title);
        if (type === 'warning' || type === 'warn') return toastWarn(msg, title);
        if (type === 'info') return toastInfo(msg, title);
        return toastError(msg, title); // default error
    }

    document.addEventListener('DOMContentLoaded', function () {
        const t = popToastAfterReload();
        if (t) showToast(t);
    });

    function toastFromPayload(payload, fallbackTitle, fallbackMsg) {
        if (payload?.toast) { showToast(payload.toast); return true; }
        if (payload?.message) { toastError(payload.message, fallbackTitle || 'Error'); return true; }
        toastError(fallbackMsg || 'Request failed.', fallbackTitle || 'Error');
        return true;
    }

    async function confirmBox(message, title) {
        if (UI?.confirm?.basic) return await UI.confirm.basic(message, { title: title ?? 'Confirm', okText: 'Yes', cancelText: 'No' });
        return window.confirm(message);
    }

    function clearErrors(prefix) {
        document.querySelectorAll(`[id^="err-${prefix}"]`).forEach(el => el.textContent = '');
    }

    function unwrapResult(json) {
        if (json && typeof json === 'object' && ('data' in json || 'toast' in json || 'ok' in json)) {
            return { data: json.data ?? null, toast: json.toast ?? null, ok: json.ok ?? null };
        }
        return { data: json, toast: null, ok: true };
    }

    async function postJson(url, body) {
        const token = $('antiForgeryToken')?.value;

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

    // =========================
    // Shared helpers for lists
    // =========================
    function fmtDate(d) { return d ? String(d) : '—'; }

    function esc(s) {
        const div = document.createElement('div');
        div.textContent = s ?? '';
        return div.innerHTML;
    }

    function safeTabShow(btnId) {
        const btn = $(btnId);
        if (!btn) return;
        bootstrap.Tab.getOrCreateInstance(btn).show();
    }

    // =========================
    // Quotes state + UI
    // =========================
    const quotesState = {
        projectId: null,
        page: 1,
        pageSize: 10,
        q: null,
        loadedOnce: false
    };

    function setQuotesLoading(on) {
        $('vpQuotesLoading')?.classList.toggle('d-none', !on);
    }

    function setQuotesEmpty(on) {
        $('vpQuotesEmpty')?.classList.toggle('d-none', !on);
    }

    function setQuotesTableVisible(on) {
        $('vpQuotesTable')?.classList.toggle('d-none', !on);
        $('vpQuotesPagerWrap')?.classList.toggle('d-none', !on);
    }

    function quoteStatusBadge(st) {
        const s = (st ?? '').toString().toLowerCase();
        if (s === 'accepted') return `<span class="badge bg-success bg-opacity-10 text-success">Accepted</span>`;
        if (s === 'rejected') return `<span class="badge bg-danger bg-opacity-10 text-danger">Rejected</span>`;
        if (s === 'sent') return `<span class="badge bg-info bg-opacity-10 text-info">Sent</span>`;
        return `<span class="badge bg-warning bg-opacity-10 text-warning">Draft</span>`;
    }

    function buildQuotesPager(res) {
        const ul = $('vpQuotesPager');
        if (!ul) return;
        ul.innerHTML = '';

        const totalPages = res.totalPages ?? res.TotalPages ?? 0;
        const page = res.page ?? res.Page ?? 1;

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

        const joiner = urlBase.includes('?') ? '&' : '?';
        const url =
            `${urlBase}${joiner}` +
            `projectId=${encodeURIComponent(quotesState.projectId)}` +
            `&p=${encodeURIComponent(quotesState.page)}` +
            `&pageSize=${encodeURIComponent(quotesState.pageSize)}` +
            `&q=${encodeURIComponent(quotesState.q || '')}`;

        try {
            const res = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } });
            const json = await res.json();

            if (!res.ok) {
                toastFromPayload(json, 'Error', 'Failed to load quotes.');
                return;
            }

            const data = json.data ?? json;
            const items = data.items ?? data.Items ?? [];

            const tbody = $('vpQuotesTbody');
            if (tbody) tbody.innerHTML = '';

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
                    <td class="text-end">${Number(total).toFixed(2)}</td>
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

    function openQuotesTabAndLoad() {
        safeTabShow('vp-tab-quotes');

        const a = $('vp-newQuoteBtn');
        if (a && quotesState.projectId) a.href = `/Quotes/Create?projectId=${encodeURIComponent(quotesState.projectId)}`;

        loadQuotes({ page: 1, q: $('vp-quotes-q')?.value?.trim() || '' });
    }

    // =========================
    // Invoices state + UI
    // =========================
    const invoicesState = {
        projectId: null,
        page: 1,
        pageSize: 10,
        q: null,
        loadedOnce: false
    };

    function setInvoicesLoading(on) {
        $('vpInvoicesLoading')?.classList.toggle('d-none', !on);
    }

    function setInvoicesEmpty(on) {
        $('vpInvoicesEmpty')?.classList.toggle('d-none', !on);
    }

    function setInvoicesVisible(on) {
        $('vpInvoicesTable')?.classList.toggle('d-none', !on);
        $('vpInvoicesPagerWrap')?.classList.toggle('d-none', !on);
    }

    function invoiceStatusBadge(st) {
        const s = (st ?? '').toString().toLowerCase();
        if (s === 'paid') return `<span class="badge bg-success bg-opacity-10 text-success">Paid</span>`;
        if (s === 'void') return `<span class="badge bg-secondary bg-opacity-10 text-secondary">Void</span>`;
        if (s === 'issued') return `<span class="badge bg-info bg-opacity-10 text-info">Issued</span>`;
        return `<span class="badge bg-warning bg-opacity-10 text-warning">Draft</span>`;
    }

    function buildInvoicesPager(res) {
        const ul = $('vpInvoicesPager');
        if (!ul) return;
        ul.innerHTML = '';

        const totalPages = res.totalPages ?? res.TotalPages ?? 0;
        const page = res.page ?? res.Page ?? 1;

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

        const joiner = urlBase.includes('?') ? '&' : '?';
        const url =
            `${urlBase}${joiner}` +
            `projectId=${encodeURIComponent(invoicesState.projectId)}` +
            `&p=${encodeURIComponent(invoicesState.page)}` +
            `&pageSize=${encodeURIComponent(invoicesState.pageSize)}` +
            `&q=${encodeURIComponent(invoicesState.q || '')}`;

        try {
            const res = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } });
            const json = await res.json();

            if (!res.ok) {
                toastFromPayload(json, 'Error', 'Failed to load invoices.');
                return;
            }

            const data = json.data ?? json;
            const items = data.items ?? data.Items ?? [];

            const tbody = $('vpInvoicesTbody');
            if (tbody) tbody.innerHTML = '';

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

                const tr = document.createElement('tr');
                tr.innerHTML = `
                    <td>${esc(invoiceNo)}</td>
                    <td>${invoiceStatusBadge(status)}</td>
                    <td>${esc(fmtDate(createdAt))}</td>
                    <td>${esc(fmtDate(issueDate))}</td>
                    <td>${esc(fmtDate(dueDate))}</td>
                    <td class="text-end">${Number(total).toFixed(2)}</td>
                    <td class="text-end">
                        <a class="btn btn-sm btn-outline-primary" href="/Invoices/Details?id=${encodeURIComponent(id)}">Details</a>
                    </td>
                `;
                tbody.appendChild(tr);
            }

            setInvoicesLoading(false);
            setInvoicesEmpty(false);
            setInvoicesVisible(true);
            buildInvoicesPager(data);
            invoicesState.loadedOnce = true;
        } catch (err) {
            console.error(err);
            setInvoicesLoading(false);
            toastError('Failed to load invoices.', 'Error');
        }
    }

    // =========================
    // Contract (single per project) + UI
    // =========================
    const contractOneState = {
        projectId: null,
        loadedOnce: false
    };

    function setContractLoading(on) {
        $('vpContractLoading')?.classList.toggle('d-none', !on);
    }

    function showContractEmpty(on) {
        $('vpContractEmpty')?.classList.toggle('d-none', !on);
    }

    function showContractPreview(on) {
        $('vpContractPreviewWrap')?.classList.toggle('d-none', !on);
    }

    function badgeHtml(status) {
        const s = (status || '').toString().toLowerCase();
        if (s === 'signed') return `<span class="badge bg-success bg-opacity-10 text-success">Signed</span>`;
        if (s === 'sent') return `<span class="badge bg-primary bg-opacity-10 text-primary">Sent</span>`;
        if (s === 'draft') return `<span class="badge bg-warning bg-opacity-10 text-warning">Draft</span>`;
        return `<span class="badge bg-secondary bg-opacity-10 text-secondary">${esc(status || '—')}</span>`;
    }

    async function loadProjectContractSnapshot() {
        if (!contractOneState.projectId) return;

        const urlBase = $('vcProjectContractSnapshotUrl')?.value;
        if (!urlBase) return toastError('vcProjectContractSnapshotUrl not found', 'Error');

        const btnCreate = document.getElementById('vpContractCreateBtn');
        const btnUpdate = document.getElementById('vpContractGenerateBtn');
        const editLink = document.getElementById('vpContractEditLink');
        const detailsLink = document.getElementById('vpContractDetailsLink');
        const sendBtn = document.getElementById('vpContractSendBtn');
        const itemsLink = document.getElementById('vpContractItemsLink');

        setContractLoading(true);
        showContractEmpty(false);
        showContractPreview(false);

        const joiner = urlBase.includes('?') ? '&' : '?';
        const url = `${urlBase}${joiner}projectId=${encodeURIComponent(contractOneState.projectId)}`;

        try {
            const res = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } });
            const json = await res.json();

            if (!res.ok) {
                toastFromPayload(json, 'Error', 'Failed to load contract.');
                return;
            }

            const data = json.data ?? json;

            // ---------- no contract ----------
            if (!data.exists) {
                $('vpContractTitle').textContent = 'No contract';
                $('vpContractMeta').textContent = '—';
                $('vpContractStatusBadge').innerHTML = '';

                // ✅ لا تكرر شرح داخل Preview
                $('vpContractPreview').innerHTML = '';

                btnCreate?.classList.remove('d-none');
                btnCreate.textContent = 'Add Contract';

                btnUpdate?.classList.add('d-none');
                editLink?.classList.add('d-none');
                detailsLink?.classList.add('d-none');
                sendBtn?.classList.add('d-none');
                itemsLink?.classList.add('d-none');

                // ✅ اعرض رسالة واحدة فقط
                showContractEmpty(true);
                showContractPreview(false);

                contractOneState.loadedOnce = true;
                return;
            }

            // ---------- contract exists ----------
            const status = (data.status || '').toString().toLowerCase();
            const isSigned = status === 'signed' || !!data.signedAt;
            const canUpdate = !!data.canUpdate && !isSigned;

            $('vpContractTitle').textContent = data.contractNo || 'Contract';
            $('vpContractMeta').textContent = data.signedAt ? `Signed at: ${fmtDateTime(data.signedAt)}` : 'Not signed yet';
            $('vpContractStatusBadge').innerHTML = badgeHtml(data.status);

            const cid = data.contractId || data.id || null;

            // Line Items button (قبل التوقيع فقط)
            if (itemsLink && cid && canUpdate) {
                itemsLink.href = `/Contracts/Items/Manage?contractId=${encodeURIComponent(cid)}&returnTo=items`;
                itemsLink.classList.remove('d-none');
            } else {
                itemsLink?.classList.add('d-none');
            }

            const itemsCount = Number(data.itemsCount || 0);
            const hasItems = itemsCount > 0;

            // Preview
            $('vpContractPreview').innerHTML = data.previewHtml || '<div class="text-muted">No contract terms yet.</div>';

            // ---------- header only (no line items) ----------
            if (!hasItems) {

                // ✅ خلي زر واحد فقط: Line Items
                if (itemsLink && cid && canUpdate) {
                    itemsLink.href = `/Contracts/Items/Manage?contractId=${encodeURIComponent(cid)}&toast=noItems`;
                    itemsLink.classList.remove('d-none');
                    itemsLink.textContent = 'Add line items'; // أو خلّها Line Items إذا تحب
                } else {
                    itemsLink?.classList.add('d-none');
                }

                // ❌ أخفِ زر Create بالكامل بهذه الحالة
                btnCreate?.classList.add('d-none');

                btnUpdate?.classList.add('d-none');
                editLink?.classList.add('d-none');
                detailsLink?.classList.add('d-none');
                sendBtn?.classList.add('d-none');

                // (اختياري) خلي التحذير داخل الـ Preview فقط
                $('vpContractPreview').innerHTML =
                    '<div class="alert alert-warning mb-0">This contract has no line items. Please add at least one line item to continue.</div>';

                showContractEmpty(false);
                showContractPreview(true);
                contractOneState.loadedOnce = true;
                return;
            }

            // ---------- has line items ----------
            btnCreate?.classList.add('d-none');

            // Update button
            if (btnUpdate) {
                btnUpdate.classList.remove('d-none');
                btnUpdate.disabled = !canUpdate;
                btnUpdate.textContent = canUpdate ? 'Update Contract' : 'Locked (Signed)';
            }

            // Edit link
            if (editLink) {
                editLink.href = data.editUrl || '#';
                editLink.classList.toggle('d-none', !canUpdate);
                editLink.textContent = 'Edit';
            }

            // Details link (يظهر فقط إذا مو Signed حسب طلبك)
            if (detailsLink) {
                let href = data.detailsUrl || '';
                if (!href && cid) href = `/Contracts/Details?id=${encodeURIComponent(cid)}`;
                detailsLink.href = href || '#';
                detailsLink.classList.toggle('d-none', isSigned);
            }

            // Send button (يظهر فقط إذا مو Signed)
            if (isSigned) sendBtn?.classList.add('d-none');
            else sendBtn?.classList.remove('d-none');
            // Send button (يظهر فقط إذا مو Signed) + يتفعل إذا hasItems
            if (sendBtn) {
                if (isSigned) {
                    sendBtn.classList.add('d-none');
                    sendBtn.disabled = true;
                } else {
                    sendBtn.classList.remove('d-none');
                    sendBtn.disabled = !hasItems; // ✅ شرطك + احترافي
                }
            }

            showContractEmpty(false);
            showContractPreview(true);
            contractOneState.loadedOnce = true;
        } catch (err) {
            console.error(err);
            toastError('Failed to load contract.', 'Error');
        } finally {
            setContractLoading(false);
        }
    }

    // Generate/Update
    document.getElementById('vpContractGenerateBtn')?.addEventListener('click', async function () {
        if (!contractOneState.projectId) return;

        const urlBase = document.getElementById('vcProjectContractGenerateUrl')?.value;
        if (!urlBase) return toastError('Contract generate endpoint is missing.', 'Error');

        const btn = document.getElementById('vpContractGenerateBtn');

        try {
            btn.disabled = true;
            window.UI?.loading?.show?.('Updating contract...');

            const token = document.getElementById('antiForgeryToken')?.value || '';
            const joiner = urlBase.includes('?') ? '&' : '?';
            const url = `${urlBase}${joiner}projectId=${encodeURIComponent(contractOneState.projectId)}`;

            const res = await fetch(url, {
                method: 'POST',
                headers: token
                    ? { 'RequestVerificationToken': token, 'Accept': 'application/json' }
                    : { 'Accept': 'application/json' }
            });

            const json = await res.json().catch(() => ({}));

            // ❌ فشل
            // ❌ فشل
            if (!res.ok) {

                // ✅ إذا السيرفر رجّع redirectUrl
                if (json?.data?.redirectUrl) {
                    const to = String(json.data.redirectUrl || '');

                    // ✅ نفس القاعدة: إذا toast=noItems لا تحفظ أي toast
                    if (to.includes('toast=noItems')) {
                        try { sessionStorage.removeItem('vc.toast.afterReload'); } catch { }
                    } else if (json?.toast) {
                        saveToastForReload(json.toast);
                    }

                    window.location.href = to;
                    return;
                }

                // باقي الأخطاء بدون redirect
                if (json?.toast) showToast(json.toast);
                else showToast({ type: 'error', title: 'Error', message: 'Unable to update the contract.' });

                return;
            }


            // ✅ نجاح
            if (json?.toast) showToast(json.toast);
            else showToast({ type: 'success', title: 'Success', message: 'The contract has been updated successfully.' });

            await loadProjectContractSnapshot();

        } catch (err) {
            console.error(err);
            toastError('Failed to generate contract.', 'Error');
        } finally {
            window.UI?.loading?.hide?.();
            btn.disabled = false;
        }
    });

    // helper for datetime in UI
    function fmtDateTime(v) {
        if (!v) return '—';
        try {
            const d = new Date(v);
            return d.toLocaleString();
        } catch { return String(v); }
    }


    

    
    // =========================
    // ---- main state ----
    // =========================
    let currentProject = null;
    let editingBasic = false;

    function statusBadgeHtml(st) {
        const s = (st ?? '').toString().toLowerCase();
        if (s === 'active') return `<span class="badge bg-success bg-opacity-10 text-success">Active</span>`;
        if (s === 'closed') return `<span class="badge bg-secondary bg-opacity-10 text-secondary">Closed</span>`;
        return `<span class="badge bg-warning bg-opacity-10 text-warning">Draft</span>`;
    }

    function setBasicMode(isEdit) {
        editingBasic = !!isEdit;
        clearErrors('vp-e');

        $('vp-basicView')?.classList.toggle('d-none', editingBasic);
        $('vp-basicEdit')?.classList.toggle('d-none', !editingBasic);
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

    function renderProject(p) {
        $('vp-title').textContent = p.title ?? 'Project';
        $('vp-statusBadge').innerHTML = statusBadgeHtml(p.status);
        $('vp-meta').textContent = p.id ? `ID: ${p.id}` : '';

        $('vp-customerName').textContent = p.customer?.name ?? '—';
        $('vp-customerEmail').textContent = p.customer?.email ?? '—';

        $('vp-v-title').textContent = p.title ?? '—';
        $('vp-v-desc').textContent = p.description ?? '—';
        $('vp-v-dates').textContent = `${fmtDate(p.startDate)} → ${fmtDate(p.endDate)}`;

        $('vp-e-title').value = p.title ?? '';
        $('vp-e-desc').value = p.description ?? '';
        $('vp-e-start').value = p.startDate ?? '';
        $('vp-e-end').value = p.endDate ?? '';

        setBasicMode(false);
        currentProject = p;

        document.querySelectorAll('[data-vc-action="set-status"]').forEach(btn => { btn.disabled = false; });
    }

    // ---- modal open ----
    const viewModalEl = $('ViewProjectModal');
    if (viewModalEl) {
        viewModalEl.addEventListener('show.bs.modal', async function (event) {
            setBasicMode(false);

            const btn = event.relatedTarget;
            const id = btn?.getAttribute('data-project-id');
            const openTab = btn?.getAttribute('data-open-tab') || 'overview';

            // reset states for this project
            quotesState.projectId = id;
            quotesState.page = 1;
            quotesState.q = null;
            quotesState.loadedOnce = false;
            if ($('vp-quotes-q')) $('vp-quotes-q').value = '';

            invoicesState.projectId = id;
            invoicesState.page = 1;
            invoicesState.q = null;
            invoicesState.loadedOnce = false;
            if ($('vp-invoices-q')) $('vp-invoices-q').value = '';

            contractOneState.projectId = id;
            contractOneState.loadedOnce = false;

            // update create links
            const q = $('vp-newQuoteBtn');
            if (q) q.href = id ? `/Quotes/Create?projectId=${encodeURIComponent(id)}` : '#';

            const c = $('vp-newContractBtn');
            if (c) c.href = id ? `/Contracts/Create?projectId=${encodeURIComponent(id)}` : '#';

            const i = $('vp-newInvoiceBtn');
            if (i) i.href = id ? `/Invoices/Create?projectId=${encodeURIComponent(id)}` : '#';

            // optional buttons if موجودين في تبويباتهم
            const c2 = $('vp-newContractBtn2');
            if (c2) c2.href = id ? `/Contracts/Create?projectId=${encodeURIComponent(id)}` : '#';

            const i2 = $('vp-newInvoiceBtn2');
            if (i2) i2.href = id ? `/Invoices/Create?projectId=${encodeURIComponent(id)}` : '#';

            $('vpLoading')?.classList.remove('d-none');
            $('vpBody')?.classList.add('d-none');
            if ($('vpLoading')) $('vpLoading').textContent = 'Loading...';

            try {
                const data = await fetchProjectById(id);
                renderProject(normalizeProject(data));

                $('vpLoading')?.classList.add('d-none');
                $('vpBody')?.classList.remove('d-none');

                // open tab requested
                if (openTab === 'quotes') {
                    openQuotesTabAndLoad();
                } else if (openTab === 'invoices') {
                    safeTabShow('vp-tab-invoices');
                } else if (openTab === 'contracts') {
                    safeTabShow('vp-tab-contracts');
                } else {
                    safeTabShow('vp-tab-overview');
                }
            } catch (err) {
                console.error(err);
                if ($('vpLoading')) $('vpLoading').textContent = 'Failed to load project.';
            }
        });
    }

    // ---- load on tab show (lazy)
    document.addEventListener('shown.bs.tab', function (e) {
        const target = e.target;

        if (target?.id === 'vp-tab-quotes') {
            const a = $('vp-newQuoteBtn');
            if (a && quotesState.projectId) a.href = `/Quotes/Create?projectId=${encodeURIComponent(quotesState.projectId)}`;
            if (!quotesState.loadedOnce) loadQuotes({ page: 1, q: $('vp-quotes-q')?.value?.trim() || '' });
        }

        if (target?.id === 'vp-tab-invoices') {
            const a = $('vp-newInvoiceBtn2');
            if (a && invoicesState.projectId) a.href = `/Invoices/Create?projectId=${encodeURIComponent(invoicesState.projectId)}`;
            if (!invoicesState.loadedOnce) loadInvoices({ page: 1, q: $('vp-invoices-q')?.value?.trim() || '' });
        }

        if (target?.id === 'vp-tab-contracts') {
            const a = $('vp-newContractBtn2');
            if (a && contractOneState.projectId)
                a.href = `/Contracts/Create?projectId=${encodeURIComponent(contractOneState.projectId)}`;

            if (!contractOneState.loadedOnce) loadProjectContractSnapshot();
        }

    });

    // ---- Quotes search
    $('vp-quotes-searchBtn')?.addEventListener('click', function () {
        loadQuotes({ page: 1, q: $('vp-quotes-q')?.value?.trim() || '' });
    });

    $('vp-quotes-q')?.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            loadQuotes({ page: 1, q: $('vp-quotes-q')?.value?.trim() || '' });
        }
    });

    // ---- Invoices search
    $('vp-invoices-searchBtn')?.addEventListener('click', function () {
        loadInvoices({ page: 1, q: $('vp-invoices-q')?.value?.trim() || '' });
    });

    $('vp-invoices-q')?.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            loadInvoices({ page: 1, q: $('vp-invoices-q')?.value?.trim() || '' });
        }
    });

   

    // ---- table delete ----
    document.addEventListener('click', async function (e) {
        const btn = e.target.closest('[data-vc-action="table-delete-project"]');
        if (!btn) return;

        e.preventDefault();

        const id = btn.getAttribute('data-project-id');
        const ok = await confirmBox('Are you sure you want to delete this project?', 'Confirm');
        if (!ok) return;

        const hid = $('tblDeleteProjectId');
        const form = $('tblDeleteForm');
        if (!hid || !form) return;

        hid.value = id;
        form.submit();
    });

    // ---- delegated actions ----
    document.addEventListener('click', async function (e) {
        const b = e.target.closest('[data-vc-action]');
        if (!b) return;

        const action = b.getAttribute('data-vc-action');
        if (!currentProject) return;

        if (action === 'edit-basic') { setBasicMode(true); return; }
        if (action === 'cancel-basic') { setBasicMode(false); return; }

        if (action === 'save-basic') {
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
                const r = unwrapResult(raw);

                const successToast = r.toast || { type: 'success', title: 'Success', message: 'Saved successfully.' };
                showToast(successToast);

                saveToastForReload(successToast);
                window.location.reload();
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
                    return toastError('Please fix the highlighted fields.', 'Validation');
                }

                toastFromPayload(err?.payload, 'Error', 'Failed to save.');
            }
            return;
        }

        if (action === 'set-status') {
            const url = $('vcChangeStatusUrl')?.value;
            if (!url) return toastError('vcChangeStatusUrl not found', 'Error');

            const st = b.getAttribute('data-status');

            try {
                const raw = await postJson(url, { projectId: currentProject.id, status: st });
                const r = unwrapResult(raw);

                const successToast = r.toast || { type: 'success', title: 'Success', message: 'Status updated.' };
                showToast(successToast);

                saveToastForReload(successToast);
                window.location.reload();
            } catch (err) {
                console.error(err);
                toastFromPayload(err?.payload, 'Error', 'Failed to update status.');
            }
            return;
        }
    });
    document.getElementById('vpContractCreateBtn')?.addEventListener('click', async function () {
        if (!contractOneState.projectId) return;

        const urlBase = document.getElementById('vcProjectContractCreateUrl')?.value;
        if (!urlBase) return toastError('Create endpoint is missing.', 'Error');

        const btn = document.getElementById('vpContractCreateBtn');

        try {
            btn.disabled = true;
            window.UI?.loading?.show?.('Preparing line items...');

            const token = document.getElementById('antiForgeryToken')?.value || '';
            const joiner = urlBase.includes('?') ? '&' : '?';
            const url = `${urlBase}${joiner}projectId=${encodeURIComponent(contractOneState.projectId)}`;

            const res = await fetch(url, {
                method: 'POST',
                headers: {
                    ...(token ? { 'RequestVerificationToken': token } : {}),
                    'Accept': 'application/json'
                }
            });

            const json = await res.json().catch(() => ({}));
            const data = json?.data || {};

            // ✅ redirectUrl priority + save toast for the destination page
            if (data.redirectUrl) {
                const u = new URL(data.redirectUrl, window.location.origin);

                // أضف toast=noItems دائماً عند التحويل إلى Manage بعد إنشاء/إجبار إضافة line items
                u.searchParams.set('toast', 'noItems');

                // امنع أي توست محفوظ سابقاً (حتى لا يظهر الأخضر أو غيره)
                try { sessionStorage.removeItem('vc.toast.afterReload'); } catch { }

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
            window.UI?.loading?.hide?.();
            btn.disabled = false;
        }
    });
    // =========================
    // ✅ Send Contract Email (button binding)
    // =========================
    document.getElementById('vpContractSendBtn')?.addEventListener('click', async function () {
        if (!contractOneState.projectId) return;

        const urlBase = document.getElementById('vcProjectContractSendUrl')?.value;
        if (!urlBase) return toastError('vcProjectContractSendUrl not found', 'Error');

        const btn = document.getElementById('vpContractSendBtn');

        try {
            btn.disabled = true;
            window.UI?.loading?.show?.('Sending contract email...');

            const token = document.getElementById('antiForgeryToken')?.value || '';
            const joiner = urlBase.includes('?') ? '&' : '?';
            const url = `${urlBase}${joiner}projectId=${encodeURIComponent(contractOneState.projectId)}`;

            const res = await fetch(url, {
                method: 'POST',
                headers: {
                    ...(token ? { 'RequestVerificationToken': token } : {}),
                    'Accept': 'application/json'
                }
            });

            const json = await res.json().catch(() => ({}));

            if (!res.ok) {
                if (json?.toast) showToast(json.toast);
                else showToast({ type: 'error', title: 'Error', message: 'Failed to send contract email.' });
                return;
            }

            if (json?.toast) showToast(json.toast);
            else showToast({ type: 'success', title: 'Sent', message: 'Contract email sent successfully.' });

            await loadProjectContractSnapshot();

        } catch (err) {
            console.error(err);
            toastError('Failed to send contract email.', 'Error');
        } finally {
            window.UI?.loading?.hide?.();
            btn.disabled = false;
        }
    });

})();
