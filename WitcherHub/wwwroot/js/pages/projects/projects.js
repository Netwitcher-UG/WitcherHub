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
    // Quotes: fetching + rendering inside modal
    // =========================
    const quotesState = {
        projectId: null,
        page: 1,
        pageSize: 10,
        q: null,
        loadedOnce: false
    };

    function fmtDate(d) { return d ? String(d) : '—'; }
    function esc(s) {
        const div = document.createElement('div');
        div.textContent = s ?? '';
        return div.innerHTML;
    }

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

        // show small window of pages
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

            const data = json.data ?? json; // handler returns {ok:true,data:...}
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

            // render rows
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
        // activate tab
        const btn = $('vp-tab-quotes');
        if (btn) {
            // bootstrap tab
            const tab = bootstrap.Tab.getOrCreateInstance(btn);
            tab.show();
        }

        // update New Quote link
        const a = $('vp-newQuoteBtn');
        if (a && quotesState.projectId) a.href = `/Quotes/Create?projectId=${encodeURIComponent(quotesState.projectId)}`;

        // load quotes once or always (هنا نحمّل دائماً لتكون fresh)
        loadQuotes({ page: 1, q: $('vp-quotes-q')?.value?.trim() || '' });
    }

    // =========================
    // ---- state ----
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

            // reset quotes state for this project
            quotesState.projectId = id;
            quotesState.page = 1;
            quotesState.q = null;
            quotesState.loadedOnce = false;
            if ($('vp-quotes-q')) $('vp-quotes-q').value = '';

            $('vpLoading').classList.remove('d-none');
            $('vpBody').classList.add('d-none');
            $('vpLoading').textContent = 'Loading...';

            try {
                const data = await fetchProjectById(id);
                renderProject(normalizeProject(data));

                $('vpLoading').classList.add('d-none');
                $('vpBody').classList.remove('d-none');

                // open tab requested
                if (openTab === 'quotes') {
                    openQuotesTabAndLoad();
                } else {
                    // show overview
                    const ov = $('vp-tab-overview');
                    if (ov) bootstrap.Tab.getOrCreateInstance(ov).show();
                }
            } catch (err) {
                console.error(err);
                $('vpLoading').textContent = 'Failed to load project.';
            }
        });
    }

    // ---- quotes tab: load on first show (when user clicks tab)
    document.addEventListener('shown.bs.tab', function (e) {
        const target = e.target;
        if (target && target.id === 'vp-tab-quotes') {
            const a = $('vp-newQuoteBtn');
            if (a && quotesState.projectId) a.href = `/Quotes/Create?projectId=${encodeURIComponent(quotesState.projectId)}`;

            // load when user opens quotes tab (if not loaded)
            if (!quotesState.loadedOnce) loadQuotes({ page: 1, q: $('vp-quotes-q')?.value?.trim() || '' });
        }
    });

    // quotes search button
    $('vp-quotes-searchBtn')?.addEventListener('click', function () {
        loadQuotes({ page: 1, q: $('vp-quotes-q')?.value?.trim() || '' });
    });

    // search by Enter
    $('vp-quotes-q')?.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            loadQuotes({ page: 1, q: $('vp-quotes-q')?.value?.trim() || '' });
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

})();
