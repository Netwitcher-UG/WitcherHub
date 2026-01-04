(function () {
    'use strict';
    window.UI = window.UI || {};
    const UI = window.UI;

    // ---------- Maps ----------
    const mapUpdateBasic = {
        "Service.Name": "vs-basic-name",
        "Name": "vs-basic-name",

        "Service.DefaultCurrency": "vs-basic-currency",
        "DefaultCurrency": "vs-basic-currency",

        "Service.ServiceType": "vs-basic-serviceType",
        "ServiceType": "vs-basic-serviceType",

        "Service.PricingModel": "vs-basic-pricingModel",
        "PricingModel": "vs-basic-pricingModel",

        "Service.BasePrice": "vs-basic-basePrice",
        "BasePrice": "vs-basic-basePrice",

        "Service.IsActive": "vs-basic-active",
        "IsActive": "vs-basic-active",

        "Service.ConfigSchemaJson": "vs-basic-config",
        "ConfigSchemaJson": "vs-basic-config",
    };

    const mapAddRule = {
        "Rule.Name": "vs-add-rule-name",
        "Name": "vs-add-rule-name",

        "Rule.Priority": "vs-add-rule-priority",
        "Priority": "vs-add-rule-priority",

        "Rule.Scope": "vs-add-rule-scope",
        "Scope": "vs-add-rule-scope",

        "Rule.Action": "vs-add-rule-action",
        "Action": "vs-add-rule-action",

        "Rule.ValueExpr": "vs-add-rule-valueExpr",
        "ValueExpr": "vs-add-rule-valueExpr",

        "Rule.ConditionExpr": "vs-add-rule-conditionExpr",
        "ConditionExpr": "vs-add-rule-conditionExpr",

        "Rule.Label": "vs-add-rule-label",
        "Label": "vs-add-rule-label",

        "Rule.ValidFrom": "vs-add-rule-validFrom",
        "ValidFrom": "vs-add-rule-validFrom",

        "Rule.ValidTo": "vs-add-rule-validTo",
        "ValidTo": "vs-add-rule-validTo",
    };

    function mapUpdateRule(idx) {
        return {
            "Rule.Name": `vs-rule-${idx}-name`,
            "Name": `vs-rule-${idx}-name`,

            "Rule.Priority": `vs-rule-${idx}-priority`,
            "Priority": `vs-rule-${idx}-priority`,

            "Rule.Scope": `vs-rule-${idx}-scope`,
            "Scope": `vs-rule-${idx}-scope`,

            "Rule.Action": `vs-rule-${idx}-action`,
            "Action": `vs-rule-${idx}-action`,

            "Rule.ValueExpr": `vs-rule-${idx}-valueExpr`,
            "ValueExpr": `vs-rule-${idx}-valueExpr`,

            "Rule.ConditionExpr": `vs-rule-${idx}-conditionExpr`,
            "ConditionExpr": `vs-rule-${idx}-conditionExpr`,

            "Rule.Label": `vs-rule-${idx}-label`,
            "Label": `vs-rule-${idx}-label`,

            "Rule.ValidFrom": `vs-rule-${idx}-validFrom`,
            "ValidFrom": `vs-rule-${idx}-validFrom`,

            "Rule.ValidTo": `vs-rule-${idx}-validTo`,
            "ValidTo": `vs-rule-${idx}-validTo`,

            "Rule.IsActive": `vs-rule-${idx}-active`,
            "IsActive": `vs-rule-${idx}-active`,
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
    function $(id) { return document.getElementById(id); }

    function toastSuccess(msg, title) { UI?.toast?.success ? UI.toast.success(msg, title) : alert((title ? title + ": " : "") + msg); }
    function toastInfo(msg, title) { UI?.toast?.info ? UI.toast.info(msg, title) : alert((title ? title + ": " : "") + msg); }
    function toastError(msg, title) { UI?.toast?.error ? UI.toast.error(msg, title) : alert((title ? title + ": " : "") + msg); }

    async function confirmBox(message, title) {
        if (UI?.confirm?.basic) return await UI.confirm.basic(message, { title: title ?? 'Confirm', okText: 'Yes', cancelText: 'No' });
        return window.confirm(message);
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

    function setEnumSelect(selectEl, value) {
        if (!selectEl) return;
        const v = value ?? '';
        selectEl.value = String(v);

        if (selectEl.value !== String(v) && typeof v === 'string') {
            const target = v.trim().toLowerCase();
            const opt = Array.from(selectEl.options).find(o => (o.textContent ?? '').trim().toLowerCase() === target);
            if (opt) selectEl.value = opt.value;
        }
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
            throw { status: res.status, payload };
        }

        return await res.json();
    }

    async function fetchServiceById(id) {
        const baseUrl = document.getElementById('vcServiceUrl')?.value;
        if (!baseUrl) throw new Error('vcServiceUrl not found');

        const joiner = baseUrl.includes('?') ? '&' : '?';
        const url = `${baseUrl}${joiner}id=${encodeURIComponent(id)}`;

        const res = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);

        const data = await res.json();
        return normalizeService(data);
    }

    function normalizeService(data) {
        if (!data) return null;

        const root = data.service ?? data.Service ?? data;

        const rules = root.pricingRules ?? root.PricingRules ?? [];
        return {
            id: root.id ?? root.Id,
            name: root.name ?? root.Name,
            serviceType: root.serviceType ?? root.ServiceType,
            pricingModel: root.pricingModel ?? root.PricingModel,
            basePrice: root.basePrice ?? root.BasePrice,
            defaultCurrency: root.defaultCurrency ?? root.DefaultCurrency,
            isActive: root.isActive ?? root.IsActive,
            configSchema: root.configSchema ?? root.ConfigSchema,
            pricingRules: (rules || []).map(r => ({
                id: r.id ?? r.Id,
                name: r.name ?? r.Name,
                priority: r.priority ?? r.Priority,
                conditionExpr: r.conditionExpr ?? r.ConditionExpr,
                action: r.action ?? r.Action,
                valueExpr: r.valueExpr ?? r.ValueExpr,
                label: r.label ?? r.Label,
                scope: r.scope ?? r.Scope,
                isActive: r.isActive ?? r.IsActive,
                validFrom: r.validFrom ?? r.ValidFrom,
                validTo: r.validTo ?? r.ValidTo
            }))
        };
    }

    // ---------- State ----------
    let currentService = null;
    let editingRuleIndex = null;
    let editingBasic = false;

    function setBasicMode(isEdit) {
        editingBasic = !!isEdit;
        clearErrors('vs-basic');

        $('vs-basicView')?.classList.toggle('d-none', editingBasic);
        $('vs-basicEdit')?.classList.toggle('d-none', !editingBasic);
    }

    function activeBadgeHtml(active) {
        return active
            ? `<span class="badge bg-success bg-opacity-10 text-success">Active</span>`
            : `<span class="badge bg-secondary bg-opacity-10 text-secondary">Inactive</span>`;
    }

    function renderRules(list) {
        const wrap = $('vs-ruleList');
        const count = $('vs-ruleCount');
        if (count) count.textContent = (list?.length ?? 0);

        if (!wrap) return;

        if (!list || !list.length) {
            wrap.innerHTML = `<div class="text-muted">No rules.</div>`;
            return;
        }

        const actionOptions = $('vsRuleActionOptions')?.innerHTML ?? '';

        wrap.innerHTML = list.map((r, idx) => {
            const isEditing = (editingRuleIndex === idx);
            const activeTxt = r.isActive ? 'Yes' : 'No';

            return `
        <div class="card rounded-4 border bg-transparent shadow-none mb-2">
          <div class="card-body py-3">
            <div class="d-flex align-items-start justify-content-between gap-3">
              <div class="flex-grow-1">

                <div class="${isEditing ? 'd-none' : ''}">
                  <div class="fw-semibold">${esc(r.name ?? '—')}</div>
                  <div class="text-muted small">
                    Priority: ${esc(r.priority)} • Scope: ${esc(r.scope)} • Active: ${activeTxt}
                  </div>
                </div>

                <div class="${isEditing ? '' : 'd-none'}">
                  <div class="row g-2">

                    <div class="col-12 col-md-6">
                      <input class="form-control form-control-sm" id="vs-rule-${idx}-name" value="${esc(r.name ?? '')}" placeholder="Name" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-name"></div>
                    </div>

                    <div class="col-6 col-md-3">
                      <input class="form-control form-control-sm" id="vs-rule-${idx}-priority" value="${esc(r.priority ?? 100)}" placeholder="Priority" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-priority"></div>
                    </div>

                    <div class="col-6 col-md-3">
                      <select class="form-select form-select-sm" id="vs-rule-${idx}-scope">
                        <option value="LINE_ITEM">LINE_ITEM</option>
                        <option value="INVOICE">INVOICE</option>
                      </select>
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-scope"></div>
                    </div>

                    <div class="col-12 col-md-4">
                      <select class="form-select form-select-sm" id="vs-rule-${idx}-action">
                        ${actionOptions}
                      </select>
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-action"></div>
                    </div>

                    <div class="col-12 col-md-8">
                      <input class="form-control form-control-sm" id="vs-rule-${idx}-valueExpr" value="${esc(r.valueExpr ?? '0')}" placeholder="ValueExpr" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-valueExpr"></div>
                    </div>

                    <div class="col-12">
                      <input class="form-control form-control-sm" id="vs-rule-${idx}-conditionExpr" value="${esc(r.conditionExpr ?? 'true')}" placeholder="ConditionExpr" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-conditionExpr"></div>
                    </div>

                    <div class="col-12 col-md-6">
                      <input class="form-control form-control-sm" id="vs-rule-${idx}-label" value="${esc(r.label ?? '')}" placeholder="Label (optional)" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-label"></div>
                    </div>

                    <div class="col-6 col-md-3">
                      <input type="date" class="form-control form-control-sm" id="vs-rule-${idx}-validFrom" value="${esc(r.validFrom ?? '')}" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-validFrom"></div>
                    </div>

                    <div class="col-6 col-md-3">
                      <input type="date" class="form-control form-control-sm" id="vs-rule-${idx}-validTo" value="${esc(r.validTo ?? '')}" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-validTo"></div>
                    </div>

                    <div class="col-12 col-md-6 d-flex align-items-end">
                      <div class="form-check form-switch">
                        <input class="form-check-input" type="checkbox" id="vs-rule-${idx}-active" ${r.isActive ? 'checked' : ''} />
                        <label class="form-check-label">Active</label>
                      </div>
                      <div class="text-danger small mt-1 ms-3" id="err-vs-rule-${idx}-active"></div>
                    </div>

                  </div>
                </div>

              </div>

              <div class="d-flex align-items-start gap-3">
                <button type="button"
                        class="btn p-0 border-0 bg-transparent text-info ${isEditing ? 'd-none' : ''}"
                        title="Edit"
                        data-vc-action="edit-rule"
                        data-index="${idx}">
                  <i class="material-icons-outlined">edit</i>
                </button>

                <button type="button"
                        class="btn p-0 border-0 bg-transparent text-success ${isEditing ? '' : 'd-none'}"
                        title="Save"
                        data-vc-action="save-rule"
                        data-index="${idx}">
                  <i class="material-icons-outlined">check</i>
                </button>

                <button type="button"
                        class="btn p-0 border-0 bg-transparent text-muted ${isEditing ? '' : 'd-none'}"
                        title="Cancel"
                        data-vc-action="cancel-rule"
                        data-index="${idx}">
                  <i class="material-icons-outlined">close</i>
                </button>

                <button type="button"
                        class="btn p-0 border-0 bg-transparent text-danger"
                        title="Delete"
                        data-vc-action="delete-rule"
                        data-index="${idx}">
                  <i class="material-icons-outlined">delete</i>
                </button>
              </div>

            </div>
          </div>
        </div>
      `;
        }).join('');

        list.forEach((r, idx) => {
            setEnumSelect($(`vs-rule-${idx}-scope`), r.scope);
            setEnumSelect($(`vs-rule-${idx}-action`), r.action);
        });
    }

    function renderService(svc) {
        $('vs-name').textContent = svc?.name ?? '—';
        $('vs-meta').textContent = `${svc?.serviceType} • ${svc?.pricingModel} • ${svc?.basePrice} ${svc?.defaultCurrency}`;
        $('vs-activeBadge').innerHTML = activeBadgeHtml(!!svc?.isActive);
        $('vs-idText').textContent = svc?.id ? `ID: ${svc.id}` : '—';

        $('vs-v-name').textContent = svc?.name ?? '—';
        $('vs-v-type').textContent = `${svc?.serviceType ?? '—'}`;
        $('vs-v-pricing').textContent = `${svc?.pricingModel ?? '—'}`;
        $('vs-v-basePrice').textContent = `${svc?.basePrice ?? '—'}`;
        $('vs-v-currency').textContent = `${svc?.defaultCurrency ?? '—'}`;
        $('vs-v-active').textContent = svc?.isActive ? 'Yes' : 'No';

        $('vs-basic-name').value = svc?.name ?? '';
        $('vs-basic-currency').value = svc?.defaultCurrency ?? 'EUR';
        $('vs-basic-basePrice').value = (svc?.basePrice ?? 0);
        $('vs-basic-active').checked = !!svc?.isActive;

        setEnumSelect($('vs-basic-serviceType'), svc?.serviceType);
        setEnumSelect($('vs-basic-pricingModel'), svc?.pricingModel);

        $('vs-schema').textContent = svc?.configSchema ? JSON.stringify(svc.configSchema, null, 2) : '';
        $('vs-basic-config').value = ''; // فارغة = لا تغيير

        setBasicMode(false);
        renderRules(svc?.pricingRules ?? []);

        currentService = svc;
    }

    // ---------- Modal open ----------
    const viewModalEl = $('ViewServiceModal');
    if (viewModalEl) {
        viewModalEl.addEventListener('show.bs.modal', async function (event) {
            editingRuleIndex = null;
            setBasicMode(false);
            clearErrors('vs-add-rule');

            const btn = event.relatedTarget;
            const id = btn?.getAttribute('data-service-id');

            if (!id) {
                toastError('Missing service id.', 'Error');
                return;
            }

            $('vsLoading').classList.remove('d-none');
            $('vsBody').classList.add('d-none');
            $('vsLoading').textContent = 'Loading...';

            try {
                const svc = await fetchServiceById(id);
                renderService(svc);

                $('vsLoading').classList.add('d-none');
                $('vsBody').classList.remove('d-none');
            } catch (err) {
                console.error(err);
                $('vsLoading').textContent = 'Failed to load service.';
            }
        });
    }

    // ---------- Table delete ----------
    document.addEventListener('click', async function (e) {
        const btn = e.target.closest('[data-vc-action="table-delete-service"]');
        if (!btn) return;

        e.preventDefault();

        const id = btn.getAttribute('data-service-id');
        const ok = await confirmBox('Are you sure you want to delete this service?', 'Confirm');
        if (!ok) return;

        const hid = document.getElementById('tblDeleteServiceId');
        const form = document.getElementById('tblDeleteForm');
        if (!hid || !form) return;

        hid.value = id;
        form.submit();
    });

    // ---------- Delegated actions (basic + rules) ----------
    document.addEventListener('click', async function (e) {
        const b = e.target.closest('[data-vc-action]');
        if (!b) return;

        const action = b.getAttribute('data-vc-action');
        const idx = Number(b.getAttribute('data-index') ?? -1);

        if (!currentService) return;

        if (action === 'edit-basic') { setBasicMode(true); return; }
        if (action === 'cancel-basic') { setBasicMode(false); return; }

        if (action === 'save-basic') {
            const url = document.getElementById('vcUpdateBasicUrl')?.value;
            if (!url) { toastError('vcUpdateBasicUrl not found.', 'Error'); return; }

            clearErrors('vs-basic');

            const cfg = $('vs-basic-config')?.value ?? '';
            const cfgToSend = cfg.trim() === '' ? null : cfg; // ✅ لا تمسح بالغلط

            const payload = {
                serviceId: currentService.id,
                service: {
                    name: $('vs-basic-name')?.value?.trim() ?? currentService.name,
                    serviceType: $('vs-basic-serviceType')?.value,
                    pricingModel: $('vs-basic-pricingModel')?.value,
                    basePrice: Number($('vs-basic-basePrice')?.value ?? 0),
                    defaultCurrency: $('vs-basic-currency')?.value?.trim() ?? 'EUR',
                    isActive: !!$('vs-basic-active')?.checked,
                    configSchemaJson: cfgToSend
                }
            };

            try {
                const updatedRaw = await postJson(url, payload);
                renderService(normalizeService(updatedRaw));
                toastSuccess('Saved successfully.', 'Success');
            } catch (err) {
                console.error(err);
                if (err?.status === 400 && err?.payload?.errors) {
                    showServerErrors(err.payload.errors, mapUpdateBasic, 'vs-basic');
                    toastError('Please fix the highlighted fields.', 'Validation');
                    return;
                }
                toastError(err?.payload?.message || 'Failed to save.', 'Error');
            }
            return;
        }

        // ----- rules -----
        if (action === 'edit-rule') { editingRuleIndex = idx; renderRules(currentService.pricingRules ?? []); return; }
        if (action === 'cancel-rule') { editingRuleIndex = null; renderRules(currentService.pricingRules ?? []); return; }

        if (action === 'save-rule') {
            const url = document.getElementById('vcUpdateRuleUrl')?.value;
            if (!url) return toastError('vcUpdateRuleUrl not found', 'Error');

            const rule = currentService.pricingRules?.[idx];
            if (!rule?.id) return toastError('RuleId missing.', 'Error');

            clearErrors(`vs-rule-${idx}`);

            const payload = {
                serviceId: currentService.id,
                ruleId: rule.id,
                rule: {
                    name: $(`vs-rule-${idx}-name`)?.value?.trim() ?? '',
                    priority: Number($(`vs-rule-${idx}-priority`)?.value ?? 100),
                    scope: $(`vs-rule-${idx}-scope`)?.value ?? 'LINE_ITEM',
                    action: $(`vs-rule-${idx}-action`)?.value,
                    valueExpr: $(`vs-rule-${idx}-valueExpr`)?.value?.trim() ?? '0',
                    conditionExpr: $(`vs-rule-${idx}-conditionExpr`)?.value?.trim() ?? 'true',
                    label: $(`vs-rule-${idx}-label`)?.value?.trim() || null,
                    isActive: !!$(`vs-rule-${idx}-active`)?.checked,
                    validFrom: $(`vs-rule-${idx}-validFrom`)?.value || null,
                    validTo: $(`vs-rule-${idx}-validTo`)?.value || null
                }
            };

            try {
                const updatedRaw = await postJson(url, payload);
                editingRuleIndex = null;
                renderService(normalizeService(updatedRaw));
                toastSuccess('Rule updated.', 'Success');
            } catch (err) {
                console.error(err);
                if (err?.status === 400 && err?.payload?.errors) {
                    showServerErrors(err.payload.errors, mapUpdateRule(idx), `vs-rule-${idx}`);
                    toastError('Fix errors.', 'Validation');
                    return;
                }
                toastError(err?.payload?.message || 'Failed to update rule.', 'Error');
            }
            return;
        }

        if (action === 'delete-rule') {
            const ok = await confirmBox('Delete this rule?', 'Confirm');
            if (!ok) return;

            const url = document.getElementById('vcDeleteRuleUrl')?.value;
            if (!url) return toastError('vcDeleteRuleUrl not found', 'Error');

            const rule = currentService.pricingRules?.[idx];
            if (!rule?.id) return toastError('RuleId missing.', 'Error');

            try {
                const updatedRaw = await postJson(url, { serviceId: currentService.id, ruleId: rule.id });
                editingRuleIndex = null;
                renderService(normalizeService(updatedRaw));
                toastSuccess('Rule deleted.', 'Success');
            } catch (err) {
                console.error(err);
                toastError(err?.payload?.message || 'Failed to delete rule.', 'Error');
            }
            return;
        }
    });

    // ---------- Add Rule form ----------
    const addRuleForm = $('vsAddRuleForm');
    if (addRuleForm) {
        addRuleForm.addEventListener('submit', async function (e) {
            e.preventDefault();
            if (!currentService) return;

            const url = document.getElementById('vcAddRuleUrl')?.value;
            if (!url) return toastError('vcAddRuleUrl not found', 'Error');

            clearErrors('vs-add-rule');

            const payload = {
                serviceId: currentService.id,
                rule: {
                    name: $('vs-add-rule-name')?.value?.trim() ?? '',
                    priority: Number($('vs-add-rule-priority')?.value ?? 100),
                    scope: $('vs-add-rule-scope')?.value ?? 'LINE_ITEM',
                    action: $('vs-add-rule-action')?.value,
                    valueExpr: $('vs-add-rule-valueExpr')?.value?.trim() ?? '0',
                    conditionExpr: $('vs-add-rule-conditionExpr')?.value?.trim() ?? 'true',
                    label: $('vs-add-rule-label')?.value?.trim() || null,
                    isActive: !!$('vs-add-rule-active')?.checked,
                    validFrom: $('vs-add-rule-validFrom')?.value || null,
                    validTo: $('vs-add-rule-validTo')?.value || null
                }
            };

            try {
                const updatedRaw = await postJson(url, payload);
                renderService(normalizeService(updatedRaw));

                addRuleForm.reset();
                const c = $('vs-addRuleCollapse');
                if (c) bootstrap.Collapse.getOrCreateInstance(c, { toggle: false }).hide();

                toastSuccess('Rule saved.', 'Success');
            } catch (err) {
                console.error(err);
                if (err?.status === 400 && err?.payload?.errors) {
                    showServerErrors(err.payload.errors, mapAddRule, 'vs-add-rule');
                    toastError('Fix errors.', 'Validation');
                    return;
                }
                toastError(err?.payload?.message || 'Failed to add rule.', 'Error');
            }
        });
    }

})();
