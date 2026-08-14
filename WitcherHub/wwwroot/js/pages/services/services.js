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

        "Service.DefaultUnitName": "vs-basic-defaultUnitName",
        "DefaultUnitName": "vs-basic-defaultUnitName",

        "Service.DefaultDescription": "vs-basic-defaultDescription",
        "DefaultDescription": "vs-basic-defaultDescription",

        "Service.ServiceType": "vs-basic-serviceType",
        "ServiceType": "vs-basic-serviceType",

        "Service.PricingModel": "vs-basic-pricingModel",
        "PricingModel": "vs-basic-pricingModel",

        "Service.BasePrice": "vs-basic-basePrice",
        "BasePrice": "vs-basic-basePrice",

        "Service.IsActive": "vs-basic-active",
        "IsActive": "vs-basic-active",

        "Service.ConfigSchemaJson": "vs-basic-config",
        "ConfigSchemaJson": "vs-basic-config"
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

        "Rule.IsActive": "vs-add-rule-active",
        "IsActive": "vs-add-rule-active",
    };
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

    function createDeleteModalHelper() {
        const modalEl = document.getElementById('DeleteServiceConfirmModal');
        const titleEl = document.getElementById('DeleteServiceConfirmModalLabel');
        const messageEl = document.getElementById('DeleteServiceConfirmModalMessage');
        const confirmBtn = document.getElementById('DeleteServiceConfirmModalSubmit');

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
        });

        return {
            open: function (title, message, confirmText, callback) {
                titleEl.textContent = title || 'Confirm';
                messageEl.textContent = message || 'Are you sure?';
                confirmBtn.textContent = confirmText || 'Delete';
                onConfirm = callback;
                modal.show();
            }
        };
    }

    const deleteModalHelper = createDeleteModalHelper();

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

    function toDateOnly(v) {
        if (!v) return '';
        if (typeof v === 'string') {
            const s = v.trim();
            const m = s.match(/^(\d{4}-\d{2}-\d{2})/);
            return m ? m[1] : '';
        }
        try {
            const d = new Date(v);
            if (!Number.isNaN(d.getTime())) return d.toISOString().slice(0, 10);
        } catch { }
        return '';
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
            defaultUnitName: root.defaultUnitName ?? root.DefaultUnitName,
            defaultDescription: root.defaultDescription ?? root.DefaultDescription,
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
                validFrom: toDateOnly(r.validFrom ?? r.ValidFrom),
                validTo: toDateOnly(r.validTo ?? r.ValidTo)
            }))
        };
    }
    // ---- row click => open service modal (overview) ----
    document.addEventListener('click', function (e) {
        // لا تفتح إذا الضغط داخل الأكشن أو أي زر/رابط/حقل
        if (e.target.closest('.vc-actions-wrap')) return;
        if (e.target.closest('button, a, input, textarea, select, label')) return;

        const tr = e.target.closest('tr');
        if (!tr) return;

        // خذ id من أي عنصر في السطر يحمل data-service-id
        const idEl = tr.querySelector('[data-service-id]');
        const id = idEl?.getAttribute('data-service-id') || '';
        if (!id) return;

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'd-none';
        btn.setAttribute('data-bs-toggle', 'modal');
        btn.setAttribute('data-bs-target', '#ViewServiceModal'); // ✅ تأكد هذا نفس ID المودال عندك
        btn.setAttribute('data-service-id', id);

        document.body.appendChild(btn);
        btn.click();
        btn.remove();
    });
    // ---------- Expr Builder (UI helper) ----------
    // VC_RULE_VARS تُبنى ديناميكياً من ConfigSchema تبع الخدمة + مفاتيح أساسية مشتركة
    const VC_BASE_RULE_VARS = [
        { key: 'qty', label: 'Quantity (qty)' },
        { key: 'basePrice', label: 'Base price (basePrice)' },
        { key: 'baseUnitPrice', label: 'Base unit price (baseUnitPrice)' },
        { key: 'subtotal', label: 'Subtotal (subtotal)' },
        { key: 'total', label: 'Total (total)' }
    ];

    let VC_RULE_VARS = [...VC_BASE_RULE_VARS];

    const VC_RULE_OPS = [
        { v: '>=', t: '>=' },
        { v: '>', t: '>' },
        { v: '<=', t: '<=' },
        { v: '<', t: '<' },
        { v: '==', t: '=' },
        { v: '!=', t: '!=' }
    ];

    function vcParseSchema(schema) {
        if (!schema) return null;
        if (typeof schema === 'string') {
            try { return JSON.parse(schema); } catch { return null; }
        }
        return schema;
    }

    function vcExtractSchemaVars(schemaObj) {
        const schema = vcParseSchema(schemaObj);
        if (!schema || typeof schema !== 'object') return [];

        const props = schema.properties || schema.Properties;
        if (!props || typeof props !== 'object') return [];

        const vars = [];
        Object.keys(props).forEach(k => {
            const def = props[k] || {};
            const title = def.title || def.Title || k;
            vars.push({ key: k, label: `${title} (${k})` });
        });

        return vars;
    }

    function vcFillFieldOptions(selectEl, vars, keepValue = true) {
        if (!selectEl) return;
        const prev = keepValue ? (selectEl.value || '') : '';
        const html = (vars || []).map(x => `<option value="${x.key}">${x.label}</option>`).join('');
        selectEl.innerHTML = `<option value="">— Select field —</option>` + html;

        if (keepValue && prev) {
            selectEl.value = prev;
            if (selectEl.value !== prev) selectEl.value = '';
        }
    }

    function vcSetRuleVarsForService(service) {
        const schemaSource = service?.configSchema ?? service?.ConfigSchema;
        const schemaVars = vcExtractSchemaVars(schemaSource);

        const map = new Map();
        VC_BASE_RULE_VARS.forEach(v => map.set(v.key, v.label));
        schemaVars.forEach(v => {
            if (!map.has(v.key)) map.set(v.key, v.label);
        });

        VC_RULE_VARS = Array.from(map.entries()).map(([key, label]) => ({ key, label }));

        // Update Add-Rule builder if it already exists
        document.querySelectorAll('#ViewServiceModal select[id$="-b-field"]').forEach(sel => {
            vcFillFieldOptions(sel, VC_RULE_VARS);
        });
    }

    function vcRefreshBuilderFieldOptions(prefix) {
        const sel = document.getElementById(`${prefix}-b-field`);
        if (!sel) return;
        vcFillFieldOptions(sel, VC_RULE_VARS);
    }

    function vcVarExpr(k) { return k ? `params["${k}"]` : ''; }

    function vcBuildCondition(field, op, value) {
        if (!field || !op) return '';
        const left = vcVarExpr(field);

        const trimmed = (value ?? '').toString().trim();
        const low = trimmed.toLowerCase();

        // booleans
        if (low === 'true' || low === 'false') {
            return `${left} ${op} ${low}`;
        }

        const num = Number(trimmed);
        const isNum = trimmed !== '' && !Number.isNaN(num);

        const right = isNum ? String(num) : `"${trimmed.replaceAll('"', '\\"')}"`;
        return `${left} ${op} ${right}`;
    }

    function vcBuildValueExpr(action, amount, mode, field, threshold) {
        const act = (action ?? '').toString().toLowerCase();
        const amtTxt = (amount ?? '').toString().trim();

        // Discount: نتركها كما هي (0.10) بدون فرض رقم
        if (act === 'discount') return amtTxt === '' ? '' : amtTxt;

        if (amtTxt === '') return '';

        const a = Number(amtTxt);
        if (Number.isNaN(a)) return '';

        const f = vcVarExpr(field);

        if (mode === 'extra_over') {
            if (!field) return '';
            const thrTxt = (threshold ?? '').toString().trim();
            if (thrTxt === '') return '';
            const thr = Number(thrTxt);
            if (Number.isNaN(thr)) return '';
            return `(${f} - ${thr}) * ${a}`;
        }
        if (mode === 'per_unit') {
            if (!field) return '';
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
      <input class="form-control form-control-sm" id="${prefix}-b-val" placeholder="e.g. 3 or true" />
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

    function vcStripOuterParens(s) {
        if (!s) return s;
        let t = s.trim();
        while (t.startsWith('(') && t.endsWith(')')) {
            let depth = 0;
            let ok = true;
            for (let i = 0; i < t.length; i++) {
                const ch = t[i];
                if (ch === '(') depth++;
                else if (ch === ')') depth--;
                if (depth === 0 && i < t.length - 1) { ok = false; break; }
            }
            if (!ok) break;
            t = t.substring(1, t.length - 1).trim();
        }
        return t;
    }

    function vcTryParseConditionExpr(expr) {
        const s0 = vcStripOuterParens((expr ?? '').toString());
        const s = s0.replaceAll('\\"', '"').trim();

        const m = s.match(/^params\s*\[\s*["']([^"']+)["']\s*\]\s*(>=|<=|==|!=|>|<)\s*(.+)$/i);
        if (!m) return null;

        const field = m[1];
        const op = m[2];
        let raw = (m[3] ?? '').trim();

        raw = vcStripOuterParens(raw);

        if ((raw.startsWith('"') && raw.endsWith('"')) || (raw.startsWith("'") && raw.endsWith("'"))) {
            raw = raw.substring(1, raw.length - 1).replaceAll('\\"', '"').replaceAll("\\'", "'");
        }

        return { field, op, value: raw };
    }

    function vcTryParseValueExpr(expr, action) {
        const act = (action ?? '').toString().toLowerCase();
        let s = vcStripOuterParens((expr ?? '').toString()).trim();

        // discount رقم بسيط فقط
        if (act === 'discount') {
            const ns = s.replace(/\s+/g, '');
            if (ns !== '' && !Number.isNaN(Number(ns))) {
                return { mode: 'once', amount: s, threshold: '', fieldFromValue: '' };
            }
            return null;
        }

        const ns = s.replace(/\s+/g, '');

        // (params["x"]-5)*80
        let m = ns.match(/^\(?(params\[\s*["']([^"']+)["']\s*\]-([0-9]+(?:\.[0-9]+)?))\)?\*([0-9]+(?:\.[0-9]+)?)$/i);
        if (m) {
            return { mode: 'extra_over', fieldFromValue: m[2], threshold: m[3], amount: m[4] };
        }

        // params["x"]*80
        m = ns.match(/^params\[\s*["']([^"']+)["']\s*\]\*([0-9]+(?:\.[0-9]+)?)$/i);
        if (m) {
            return { mode: 'per_unit', fieldFromValue: m[1], threshold: '', amount: m[2] };
        }

        // once numeric literal
        if (!Number.isNaN(Number(ns)) && ns !== '') {
            return { mode: 'once', fieldFromValue: '', threshold: '', amount: s };
        }

        return null;
    }

    function vcSetMode(prefix, mode) {
        const m = mode || 'once';
        const el = document.querySelector(`input[name="${prefix}-b-mode"][value="${m}"]`);
        if (el) el.checked = true;
    }

    function vcWireBuilder(prefix, ids, options) {
        options = options || {};
        const initFromInputs = !!options.initFromInputs;

        const fieldEl = document.getElementById(`${prefix}-b-field`);
        const opEl = document.getElementById(`${prefix}-b-op`);
        const valEl = document.getElementById(`${prefix}-b-val`);
        const amtEl = document.getElementById(`${prefix}-b-amt`);
        const thrEl = document.getElementById(`${prefix}-b-threshold`);
        const previewEl = document.getElementById(`${prefix}-b-preview`);

        const condInput = document.getElementById(ids.conditionId);
        const valInput = document.getElementById(ids.valueId);

        vcRefreshBuilderFieldOptions(prefix);

        function getMode() {
            return document.querySelector(`input[name="${prefix}-b-mode"]:checked`)?.value || 'once';
        }

        function getActionTextOrValue() {
            const sel = document.getElementById(ids.actionId);
            if (!sel) return '';
            const opt = sel.options?.[sel.selectedIndex];
            return (opt?.textContent ?? sel.value ?? '').toString().trim();
        }

        function run(syncToInputs) {
            const field = fieldEl?.value;
            const op = opEl?.value;
            const v = valEl?.value;

            const mode = getMode();
            const thr = thrEl?.value;
            const amt = amtEl?.value;

            const actionName = getActionTextOrValue();

            const condBuilt = vcBuildCondition(field, op, v);
            const valBuilt = vcBuildValueExpr(actionName, amt, mode, field, thr);

            if (syncToInputs) {
                if (condInput && condBuilt) condInput.value = condBuilt;

                // لا تكتب على ValueExpr إذا المستخدم ما حط Amount (مهم جداً أثناء Edit)
                if (valInput && valBuilt !== '') valInput.value = valBuilt;
            }

            const condShown = condBuilt || condInput?.value || '(...)';
            const valShown = (valBuilt !== '' ? valBuilt : (valInput?.value || '(...)'));
            if (previewEl) previewEl.textContent = `IF ${condShown} THEN ${valShown}`;
        }

        if (initFromInputs && condInput && valInput) {
            const c = vcTryParseConditionExpr(condInput.value);
            const aName = getActionTextOrValue();
            const v = vcTryParseValueExpr(valInput.value, aName);

            const fieldExists = (f) => !!(f && Array.from(fieldEl?.options || []).some(o => o.value === f));

            if (c && fieldEl && opEl && valEl && fieldExists(c.field)) {
                fieldEl.value = c.field;
                opEl.value = c.op;
                valEl.value = (c.value ?? '').toString();
            }

            if (v && amtEl && thrEl) {
                if (v.fieldFromValue && fieldEl && fieldExists(v.fieldFromValue)) fieldEl.value = v.fieldFromValue;
                amtEl.value = (v.amount ?? '').toString();
                thrEl.value = (v.threshold ?? '').toString();
                vcSetMode(prefix, v.mode);
            }

            // إذا ما قدرنا نفهمها، خليها Advanced (حتى ما نخرب expressions)
            if (!(c && v)) {
                document.getElementById(`${prefix}-advWrap`)?.classList.remove('d-none');
            }

            run(!!(c && v));
        } else {
            run(true);
        }

        [fieldEl, opEl, valEl, amtEl, thrEl].forEach(x => x && x.addEventListener('input', () => run(true)));
        [fieldEl, opEl].forEach(x => x && x.addEventListener('change', () => run(true)));
        document.querySelectorAll(`input[name="${prefix}-b-mode"]`).forEach(r => r.addEventListener('change', () => run(true)));
        document.getElementById(ids.actionId)?.addEventListener('change', () => run(true));
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

        // إذا انحقن قبل لا تعيد (بس حدّث الـ fields)
        if (document.getElementById('vs-add-rule-builderWrap')) {
            vcRefreshBuilderFieldOptions('vs-add-rule');
            return;
        }

        const cond = document.getElementById('vs-add-rule-conditionExpr');
        const val = document.getElementById('vs-add-rule-valueExpr');
        const action = document.getElementById('vs-add-rule-action');
        if (!cond || !val || !action) return;

        const advWrap = document.createElement('div');
        advWrap.id = 'vs-add-rule-advWrap';
        advWrap.className = 'd-none';

        const condCol = cond.closest('.col-12') || cond.parentElement;
        const valCol = val.closest('.col-12') || val.parentElement;

        if (valCol) advWrap.appendChild(valCol);
        if (condCol && condCol !== valCol) advWrap.appendChild(condCol);

        const builderWrap = document.createElement('div');
        builderWrap.id = 'vs-add-rule-builderWrap';
        builderWrap.innerHTML = vcBuilderHtml('vs-add-rule');

        addWrap.prepend(advWrap);
        addWrap.prepend(builderWrap);

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
                    Priority: ${esc(r.priority)} • Scope: ${esc(scopeText(r.scope))} • Active: ${activeTxt}
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
                        <option value="LINE_ITEM">This Service</option>
<option value="INVOICE">Overall Total</option>
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

                    <!-- Advanced expressions -->
                    <div class="col-12 d-none" id="vs-rule-${idx}-advWrap">
                      <div class="row g-2">
                        <div class="col-12">
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
                  <i class="ri-edit-line"></i>
                </button>

                <button type="button"
                        class="btn p-0 border-0 bg-transparent text-success ${isEditing ? '' : 'd-none'}"
                        title="Save"
                        data-vc-action="save-rule"
                        data-index="${idx}">
                  <i class="ri-check-line"></i>
                </button>

                <button type="button"
                        class="btn p-0 border-0 bg-transparent text-muted ${isEditing ? '' : 'd-none'}"
                        title="Cancel"
                        data-vc-action="cancel-rule"
                        data-index="${idx}">
                  <i class="ri-close-line"></i>
                </button>

                <button type="button"
                        class="btn p-0 border-0 bg-transparent text-danger"
                        title="Delete"
                        data-vc-action="delete-rule"
                        data-index="${idx}">
                  <i class="ri-delete-bin-line"></i>
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

        // wire builder فقط للي عم تنعدل (وبـ init من DB)
        if (editingRuleIndex !== null && editingRuleIndex >= 0) {
            const idx = editingRuleIndex;
            vcWireBuilder(`vs-rule-${idx}`, {
                conditionId: `vs-rule-${idx}-conditionExpr`,
                valueId: `vs-rule-${idx}-valueExpr`,
                actionId: `vs-rule-${idx}-action`
            }, { initFromInputs: true });
        }
    }
    function scopeText(scope) {
        const s = String(scope ?? '').trim().toUpperCase();
        if (s === 'LINE_ITEM') return 'This Service';
        if (s === 'INVOICE') return 'Overall Total';
        return scope ?? '—';
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
        $('vs-v-defaultUnitName').textContent = svc?.defaultUnitName ?? '—';
        $('vs-v-defaultDescription').textContent = svc?.defaultDescription ?? '—';
        $('vs-basic-name').value = svc?.name ?? '';
        $('vs-basic-currency').value = svc?.defaultCurrency ?? 'EUR';
        $('vs-basic-defaultUnitName').value = svc?.defaultUnitName ?? '';
        $('vs-basic-defaultDescription').value = svc?.defaultDescription ?? '';
        $('vs-basic-basePrice').value = (svc?.basePrice ?? 0);
        $('vs-basic-active').checked = !!svc?.isActive;

        setEnumSelect($('vs-basic-serviceType'), svc?.serviceType);
        setEnumSelect($('vs-basic-pricingModel'), svc?.pricingModel);

        const schemaJson = svc?.configSchema
            ? (typeof svc.configSchema === 'string'
                ? svc.configSchema
                : JSON.stringify(svc.configSchema, null, 2))
            : '';

        $('vs-schema').textContent = schemaJson;
        $('vs-basic-config').value = schemaJson;
        document.dispatchEvent(new CustomEvent('schema-builder:load-by-textarea', {
            detail: { textarea: '#vs-basic-config' }
        }));
        // ✅ Dynamic fields from schema
        vcSetRuleVarsForService(svc);


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
    document.addEventListener('click', function (e) {
        const btn = e.target.closest('[data-vc-action="table-delete-service"]');
        if (!btn) return;

        e.preventDefault();

        const id = btn.getAttribute('data-service-id');
        const hid = document.getElementById('tblDeleteServiceId');
        const form = document.getElementById('tblDeleteForm');

        if (!id || !hid || !form) return;

        if (!deleteModalHelper) {
            hid.value = id;
            form.submit();
            return;
        }

        deleteModalHelper.open(
            'Delete service',
            'Are you sure you want to delete this service?',
            'Delete',
            async function () {
                hid.value = id;
                form.submit();
            }
        );
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

            if (prefix === 'vs-add-rule') {
                document.getElementById('vs-add-rule-advWrap')?.classList.toggle('d-none');
                return;
            }

            vcToggleAdvanced(prefix);
            return;
        }

        if (!currentService) return;

        if (action === 'edit-basic') {
            setBasicMode(true);

            document.dispatchEvent(new CustomEvent('schema-builder:load-by-textarea', {
                detail: { textarea: '#vs-basic-config' }
            }));

            return;
        }
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
                    defaultUnitName: $('vs-basic-defaultUnitName')?.value?.trim() ?? '',
                    defaultDescription: $('vs-basic-defaultDescription')?.value?.trim() ?? '',
          isActive: !!$('vs-basic-active')?.checked,
                    configSchemaJson: cfgToSend
                }
            };

            try {
                const updatedRaw = await postJson(url, payload);
                renderService(normalizeService(updatedRaw));
                toastSuccess('Saved successfully.', 'Success');
                setTimeout(() => {
                    window.location.reload();
                }, 500);
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
            const url = document.getElementById('vcDeleteRuleUrl')?.value;
            if (!url) return toastError('vcDeleteRuleUrl not found', 'Error');

            const rule = currentService.pricingRules?.[idx];
            if (!rule?.id) return toastError('RuleId missing.', 'Error');

            const doDelete = async function () {
                try {
                    const updatedRaw = await postJson(url, { serviceId: currentService.id, ruleId: rule.id });
                    editingRuleIndex = null;
                    renderService(normalizeService(updatedRaw));
                    toastSuccess('Rule deleted.', 'Success');
                } catch (err) {
                    console.error(err);
                    toastError(err?.payload?.message || 'Failed to delete rule.', 'Error');
                }
            };

            if (!deleteModalHelper) {
                await doDelete();
                return;
            }

            deleteModalHelper.open(
                'Delete rule',
                'Are you sure you want to delete this rule?',
                'Delete',
                doDelete
            );
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



    function initSearchClearButtons(root = document) {
        root.querySelectorAll('.order-search').forEach(function (form) {
            const input = form.querySelector('input[type="text"]');
            const clearBtn = form.querySelector('[data-search-clear]');
            if (!input || !clearBtn) return;

            function syncClearButton() {
                clearBtn.classList.toggle('d-none', !input.value.trim());
            }

            if (form.dataset.clearInit !== '1') {
                form.dataset.clearInit = '1';

                clearBtn.addEventListener('click', function () {
                    input.value = '';
                    syncClearButton();

                    const pageInput = form.querySelector('input[type="hidden"][name]');
                    if (pageInput) pageInput.value = '1';

                    if (typeof form.requestSubmit === 'function') {
                        form.requestSubmit();
                    } else {
                        form.submit();
                    }
                });

                input.addEventListener('input', syncClearButton);
            }

            syncClearButton();
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
            const input = host?.querySelector('.order-search input[type="text"]');
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

    (function bindServicesLiveSearch() {
        let debounceTimer = null;
        let activeController = null;

        function initServicesLiveSearch() {
            const host = document.getElementById('servicesTableCard');
            if (!host) return;

            const form = host.querySelector('.order-search');
            const input = form?.querySelector('input[type="text"]');
            if (!form || !input) return;

            initSearchClearButtons(host);

            if (form.dataset.liveSearchBound === '1') return;
            form.dataset.liveSearchBound = '1';

            async function reloadServicesTable() {
                const currentHost = document.getElementById('servicesTableCard');
                if (!currentHost) return;

                const currentForm = currentHost.querySelector('.order-search');
                const currentInput = currentForm?.querySelector('input[type="text"]');
                if (!currentForm || !currentInput) return;

                const searchState = captureSearchState(currentInput);

                const formData = new FormData(currentForm);

                formData.set(currentInput.name, currentInput.value.trim());

                const pageInput = currentForm.querySelector('input[type="hidden"][name]');
                if (pageInput) {
                    formData.set(pageInput.name, '1');
                }

                const url = new URL(window.location.href);
                url.search = '';

                for (const [key, value] of formData.entries()) {
                    const v = String(value ?? '').trim();
                    if (v) {
                        url.searchParams.set(key, v);
                    }
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
                    const newHost = doc.getElementById('servicesTableCard');
                    if (!newHost) return;

                    currentHost.outerHTML = newHost.outerHTML;

                    window.history.replaceState({}, '', url.pathname + url.search);

                    initServicesLiveSearch();
                    restoreSearchState('servicesTableCard', searchState);
                } catch (err) {
                    if (err.name === 'AbortError') return;
                    console.error('Services live search failed:', err);
                }
            }

            input.addEventListener('input', function () {
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(reloadServicesTable, 500);
            });

            form.addEventListener('submit', function (e) {
                e.preventDefault();
                clearTimeout(debounceTimer);
                reloadServicesTable();
            });
        }

        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', initServicesLiveSearch);
        } else {
            initServicesLiveSearch();
        }
    })();

    document.addEventListener('DOMContentLoaded', function () {
        const modalEl = document.getElementById('FormModal');
        if (!modalEl) return;

        const form = modalEl.querySelector('form');
        if (!form) return;

        function clearRazorValidationState(formEl) {
            if (window.jQuery) {
                const $form = window.jQuery(formEl);
                const validator = $form.data('validator');
                const unobtrusive = $form.data('unobtrusiveValidation');

                if (validator && typeof validator.resetForm === 'function') {
                    validator.resetForm();
                }

                formEl.querySelectorAll('[data-valmsg-for]').forEach(el => {
                    el.textContent = '';
                    el.classList.remove('field-validation-error');
                    el.classList.add('field-validation-valid');
                });

                formEl.querySelectorAll('[data-valmsg-summary="true"]').forEach(el => {
                    el.innerHTML = '';
                    el.classList.remove('validation-summary-errors');
                    el.classList.add('validation-summary-valid');
                });

                formEl.querySelectorAll('.input-validation-error').forEach(el => {
                    el.classList.remove('input-validation-error');
                    el.removeAttribute('aria-invalid');
                });

                if (unobtrusive && window.jQuery.validator?.unobtrusive) {
                    $form.removeData('validator');
                    $form.removeData('unobtrusiveValidation');
                    window.jQuery.validator.unobtrusive.parse(formEl);
                }
            }
        }

        function resetCreateServiceModal() {
            // reset عام أولي
            form.reset();

            // الحقول النصية والقيم المطلوبة
            const name = form.querySelector('[name="Service.Name"]');
            const currency = form.querySelector('[name="Service.DefaultCurrency"]');
            const basePrice = form.querySelector('[name="Service.BasePrice"]');
            const defaultUnit = form.querySelector('[name="Service.DefaultUnitName"]');
            const defaultDescription = form.querySelector('[name="Service.DefaultDescription"]');
            const configSchemaJson = form.querySelector('[name="Service.ConfigSchemaJson"]');
            const isActive = form.querySelector('[name="Service.IsActive"]');
            const serviceType = form.querySelector('[name="Service.ServiceType"]');
            const pricingModel = form.querySelector('[name="Service.PricingModel"]');

            if (name) name.value = '';
            if (currency) currency.value = 'EUR';
            if (basePrice) basePrice.value = '';
            if (defaultUnit) defaultUnit.value = '';
            if (defaultDescription) defaultDescription.value = '';
            if (configSchemaJson) configSchemaJson.value = '';

            if (serviceType) serviceType.selectedIndex = 0;
            if (pricingModel) pricingModel.selectedIndex = 0;

            if (isActive) {
                isActive.checked = true;
            }

            // رجّع التبويب إلى Schema Builder
            const builderTabBtn = document.getElementById('cs-create-builder-tab');
            if (builderTabBtn && window.bootstrap) {
                bootstrap.Tab.getOrCreateInstance(builderTabBtn).show();
            }

            document.dispatchEvent(new CustomEvent('schema-builder:reset', {
                detail: { container: '#csb-create' }
            }));

            clearRazorValidationState(form);
        }

        modalEl.addEventListener('hidden.bs.modal', function () {
            resetCreateServiceModal();
        });
    });
})();
