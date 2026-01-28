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

    // ---------- Expr Builder (UI helper) ----------
    // (واجهة فقط - ما بتغيّر الباك اند)
    const VC_RULE_VARS = [
        { key: 'qty', label: 'Quantity (qty)' },
        { key: 'pages', label: 'Pages (pages)' },
        { key: 'subtotal', label: 'Subtotal (subtotal)' },
        { key: 'total', label: 'Total (total)' }
    ];

    const VC_RULE_OPS = [
        { v: '>=', t: '>=' },
        { v: '>', t: '>' },
        { v: '<=', t: '<=' },
        { v: '<', t: '<' },
        { v: '==', t: '=' },
        { v: '!=', t: '!=' }
    ];

    function vcVarExpr(k) { return k ? `params["${k}"]` : ''; }

    function vcBuildCondition(field, op, value) {
        if (!field || !op) return '';
        const left = vcVarExpr(field);

        const trimmed = (value ?? '').toString().trim();
        const num = Number(trimmed);
        const isNum = trimmed !== '' && !Number.isNaN(num);

        const right = isNum ? String(num) : `"${trimmed.replaceAll('"', '\\"')}"`;
        return `${left} ${op} ${right}`;
    }

    function vcBuildValueExpr(action, amount, mode, field, threshold) {
        const act = (action ?? '').toString().toLowerCase();
        const a = Number(amount ?? 0);
        const f = vcVarExpr(field);

        // Discount: غالباً value expr نسبة مثل 0.10
        if (act === 'discount') return String(a);

        if (mode === 'extra_over') {
            const thr = Number(threshold ?? 0);
            return `(${f} - ${thr}) * ${a}`;
        }
        if (mode === 'per_unit') {
            return `${f} * ${a}`;
        }
        return String(a); // once
    }

    function vcBuilderHtml(prefix) {
        const varsOptions = VC_RULE_VARS.map(x => `<option value="${x.key}">${x.label}</option>`).join('');
        const opsOptions = VC_RULE_OPS.map(x => `<option value="${x.v}">${x.t}</option>`).join('');

        return `
<div class="mt-2">
  <div class="d-flex align-items-center justify-content-between">
    <div class="fw-semibold">Easy Builder</div>
    <button type="button"
            class="btn btn-sm btn-outline-secondary rounded-5"
            data-vc-action="toggle-advanced"
            data-prefix="${prefix}">
      Advanced
    </button>
  </div>

  <div class="row g-2 mt-1" id="${prefix}-builder">
    <div class="col-12">
      <div class="text-muted small">Build Condition/Value without writing expressions.</div>
    </div>

    <div class="col-12 col-md-4">
      <label class="form-label mb-1 small text-muted" for="${prefix}-b-field">Field</label>
      <select class="form-select form-select-sm" id="${prefix}-b-field">
        ${varsOptions}
      </select>
    </div>

    <div class="col-6 col-md-2">
      <label class="form-label mb-1 small text-muted" for="${prefix}-b-op">Operator</label>
      <select class="form-select form-select-sm" id="${prefix}-b-op">
        ${opsOptions}
      </select>
    </div>

    <div class="col-6 col-md-2">
      <label class="form-label mb-1 small text-muted" for="${prefix}-b-val">Value</label>
      <input class="form-control form-control-sm" id="${prefix}-b-val" placeholder="e.g. 3" />
    </div>

    <div class="col-12 col-md-4">
      <label class="form-label mb-1 small text-muted" for="${prefix}-b-amt">Amount / Discount</label>
      <input class="form-control form-control-sm" id="${prefix}-b-amt" placeholder="e.g. 80 or 0.10" />
    </div>

    <div class="col-12 col-md-6">
      <label class="form-label mb-1 small text-muted">Apply</label>
      <div class="d-flex gap-3 flex-wrap">
        <div class="form-check">
          <input class="form-check-input" type="radio" name="${prefix}-b-mode" id="${prefix}-b-once" value="once" checked>
          <label class="form-check-label small" for="${prefix}-b-once">Once</label>
        </div>
        <div class="form-check">
          <input class="form-check-input" type="radio" name="${prefix}-b-mode" id="${prefix}-b-per" value="per_unit">
          <label class="form-check-label small" for="${prefix}-b-per">Per unit</label>
        </div>
        <div class="form-check">
          <input class="form-check-input" type="radio" name="${prefix}-b-mode" id="${prefix}-b-extra" value="extra_over">
          <label class="form-check-label small" for="${prefix}-b-extra">Extra over threshold</label>
        </div>
      </div>
    </div>

    <div class="col-12 col-md-6">
      <label class="form-label mb-1 small text-muted" for="${prefix}-b-threshold">Threshold (for Extra over)</label>
      <input class="form-control form-control-sm" id="${prefix}-b-threshold" placeholder="e.g. 5" />
    </div>

    <div class="col-12">
      <div class="small text-muted">Preview:</div>
      <div class="small" id="${prefix}-b-preview" style="word-break:break-word;"></div>
    </div>
  </div>
</div>
`;
    }

    function vcWireBuilder(prefix, ids) {
        // ids = { conditionId, valueId, actionId }
        const fieldEl = document.getElementById(`${prefix}-b-field`);
        const opEl = document.getElementById(`${prefix}-b-op`);
        const valEl = document.getElementById(`${prefix}-b-val`);
        const amtEl = document.getElementById(`${prefix}-b-amt`);
        const thrEl = document.getElementById(`${prefix}-b-threshold`);
        const previewEl = document.getElementById(`${prefix}-b-preview`);

        const condInput = document.getElementById(ids.conditionId);
        const valInput = document.getElementById(ids.valueId);

        function getMode() {
            return document.querySelector(`input[name="${prefix}-b-mode"]:checked`)?.value || 'once';
        }

        function run() {
            const field = fieldEl?.value;
            const op = opEl?.value;
            const v = valEl?.value;

            const mode = getMode();
            const thr = thrEl?.value;
            const amt = amtEl?.value;

            const action = document.getElementById(ids.actionId)?.value || '';

            const cond = vcBuildCondition(field, op, v);
            const valExpr = vcBuildValueExpr(action, amt, mode, field, thr);

            if (condInput && cond) condInput.value = cond;
            if (valInput && valExpr) valInput.value = valExpr;

            if (previewEl) previewEl.textContent = `IF ${cond || '(...)'} THEN ${valExpr || '(...)'}`;
        }

        [fieldEl, opEl, valEl, amtEl, thrEl].forEach(x => x && x.addEventListener('input', run));
        [fieldEl, opEl].forEach(x => x && x.addEventListener('change', run));
        document.querySelectorAll(`input[name="${prefix}-b-mode"]`).forEach(r => r.addEventListener('change', run));

        document.getElementById(ids.actionId)?.addEventListener('change', run);

        run();
    }

    function vcToggleAdvanced(prefix) {
        const adv = document.getElementById(`${prefix}-advWrap`);
        if (!adv) return;
        adv.classList.toggle('d-none');
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

    // ---- Add Rule builder injection ----
    function ensureAddRuleBuilder() {
        const addWrap = document.getElementById('vsAddRuleForm');
        if (!addWrap) return;

        // إذا انحقن قبل لا تعيد
        if (document.getElementById('vs-add-rule-builderWrap')) return;

        // لازم تكون موجودة inputs الأصلية
        const cond = document.getElementById('vs-add-rule-conditionExpr');
        const val = document.getElementById('vs-add-rule-valueExpr');
        const action = document.getElementById('vs-add-rule-action');
        if (!cond || !val || !action) return;

        // لفّ الحقول الأصلية (Advanced)
        // إذا عندك wrapper جاهز بالـ cshtml ما رح يأثر، بس هون حل عام
        const advWrap = document.createElement('div');
        advWrap.id = 'vs-add-rule-advWrap';
        advWrap.className = 'd-none';

        // نقل الحقول (Condition/Value) جوّا Advanced
        // ملاحظة: ما منغيّر IDs ولا Name
        const condCol = cond.closest('.col-12') || cond.parentElement;
        const valCol = val.closest('.col-12') || val.parentElement;

        // في حال structure مختلفة، بنحافظ قد ما فينا
        if (valCol) advWrap.appendChild(valCol);
        if (condCol && condCol !== valCol) advWrap.appendChild(condCol);

        // Inject builder قبل الـ advanced
        const builderWrap = document.createElement('div');
        builderWrap.id = 'vs-add-rule-builderWrap';
        builderWrap.innerHTML = vcBuilderHtml('vs-add-rule');

        // حطهم بأول الفورم (أو قبل زر الحفظ حسب هيكل صفحتك)
        addWrap.prepend(advWrap);
        addWrap.prepend(builderWrap);

        // Wire builder to existing fields
        vcWireBuilder('vs-add-rule', {
            conditionId: 'vs-add-rule-conditionExpr',
            valueId: 'vs-add-rule-valueExpr',
            actionId: 'vs-add-rule-action'
        });
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
                  ${vcBuilderHtml(`vs-rule-${idx}`)}

                  <div class="row g-2 mt-2">

                    <div class="col-12 col-md-6">
                      <label class="form-label mb-1 small text-muted" for="vs-rule-${idx}-name">Name</label>
                      <input class="form-control form-control-sm" id="vs-rule-${idx}-name" value="${esc(r.name ?? '')}" placeholder="Name" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-name"></div>
                    </div>

                    <div class="col-6 col-md-3">
                      <label class="form-label mb-1 small text-muted" for="vs-rule-${idx}-priority">Priority</label>
                      <input class="form-control form-control-sm" id="vs-rule-${idx}-priority" value="${esc(r.priority ?? 100)}" placeholder="Priority" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-priority"></div>
                    </div>

                    <div class="col-6 col-md-3">
                      <label class="form-label mb-1 small text-muted" for="vs-rule-${idx}-scope">Scope</label>
                      <select class="form-select form-select-sm" id="vs-rule-${idx}-scope">
                        <option value="LINE_ITEM">LINE_ITEM</option>
                        <option value="INVOICE">INVOICE</option>
                      </select>
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-scope"></div>
                    </div>

                    <div class="col-12 col-md-4">
                      <label class="form-label mb-1 small text-muted" for="vs-rule-${idx}-action">Action</label>
                      <select class="form-select form-select-sm" id="vs-rule-${idx}-action">
                        ${actionOptions}
                      </select>
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-action"></div>
                    </div>

                    <div class="col-12 col-md-6">
                      <label class="form-label mb-1 small text-muted" for="vs-rule-${idx}-label">Label</label>
                      <input class="form-control form-control-sm" id="vs-rule-${idx}-label" value="${esc(r.label ?? '')}" placeholder="Label (optional)" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-label"></div>
                    </div>

                    <div class="col-6 col-md-3">
                      <label class="form-label mb-1 small text-muted" for="vs-rule-${idx}-validFrom">Valid From</label>
                      <input type="date" class="form-control form-control-sm" id="vs-rule-${idx}-validFrom" value="${esc(r.validFrom ?? '')}" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-validFrom"></div>
                    </div>

                    <div class="col-6 col-md-3">
                      <label class="form-label mb-1 small text-muted" for="vs-rule-${idx}-validTo">Valid To</label>
                      <input type="date" class="form-control form-control-sm" id="vs-rule-${idx}-validTo" value="${esc(r.validTo ?? '')}" />
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-validTo"></div>
                    </div>

                    <div class="col-12 col-md-6">
                      <label class="form-label mb-1 small text-muted d-block" for="vs-rule-${idx}-active">Active</label>
                      <div class="form-check form-switch">
                        <input class="form-check-input" type="checkbox" id="vs-rule-${idx}-active" ${r.isActive ? 'checked' : ''} />
                        <label class="form-check-label" for="vs-rule-${idx}-active">Active</label>
                      </div>
                      <div class="text-danger small mt-1" id="err-vs-rule-${idx}-active"></div>
                    </div>

                    <!-- Advanced expressions (same inputs, hidden by default) -->
                    <div class="col-12 d-none" id="vs-rule-${idx}-advWrap">
                      <div class="row g-2">
                        <div class="col-12 col-md-8">
                          <label class="form-label mb-1 small text-muted" for="vs-rule-${idx}-valueExpr">Value Expr</label>
                          <input class="form-control form-control-sm" id="vs-rule-${idx}-valueExpr" value="${esc(r.valueExpr ?? '0')}" placeholder="ValueExpr" />
                          <div class="text-danger small mt-1" id="err-vs-rule-${idx}-valueExpr"></div>
                        </div>

                        <div class="col-12">
                          <label class="form-label mb-1 small text-muted" for="vs-rule-${idx}-conditionExpr">Condition Expr</label>
                          <input class="form-control form-control-sm" id="vs-rule-${idx}-conditionExpr" value="${esc(r.conditionExpr ?? 'true')}" placeholder="ConditionExpr" />
                          <div class="text-danger small mt-1" id="err-vs-rule-${idx}-conditionExpr"></div>
                        </div>
                      </div>
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

        // wire builder فقط للي عم تنعدل
        if (editingRuleIndex !== null && editingRuleIndex >= 0) {
            const idx = editingRuleIndex;
            vcWireBuilder(`vs-rule-${idx}`, {
                conditionId: `vs-rule-${idx}-conditionExpr`,
                valueId: `vs-rule-${idx}-valueExpr`,
                actionId: `vs-rule-${idx}-action`
            });
        }
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

        // inject builder into Add Rule area
        ensureAddRuleBuilder();
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

        // toggle advanced expressions
        if (action === 'toggle-advanced') {
            const prefix = b.getAttribute('data-prefix');
            if (!prefix) return;

            // add rule advanced
            if (prefix === 'vs-add-rule') {
                document.getElementById('vs-add-rule-advWrap')?.classList.toggle('d-none');
                return;
            }

            // edit rule advanced
            vcToggleAdvanced(prefix);
            return;
        }

        if (!currentService) return;

        if (action === 'edit-basic') { setBasicMode(true); return; }
        if (action === 'cancel-basic') { setBasicMode(false); return; }

        if (action === 'save-basic') {
            const url = document.getElementById('vcUpdateBasicUrl')?.value;
            if (!url) { toastError('vcUpdateBasicUrl not found.', 'Error'); return; }

            clearErrors('vs-basic');

            const cfg = $('vs-basic-config')?.value ?? '';
            const cfgToSend = cfg.trim() === '' ? null : cfg;

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
