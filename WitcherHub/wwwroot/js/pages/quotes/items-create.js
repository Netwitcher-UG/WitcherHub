(function () {
  // -----------------------------
  // Helpers
  // -----------------------------
  function escapeHtml(s) {
    return (s || '').replace(/[&<>"']/g, c => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
  }

  function tryParseJson(raw) {
    try { return JSON.parse(raw || "{}"); } catch { return null; }
  }

  function stringifyJson(obj, pretty) {
    return JSON.stringify(obj ?? {}, null, pretty ? 2 : 0);
  }

  function dispatchAppEvent(name) {
    document.dispatchEvent(new CustomEvent(name));
  }

  function debounce(fn, wait) {
    let t = null;
    return function () {
      const ctx = this;
      const args = arguments;
      clearTimeout(t);
      t = setTimeout(() => fn.apply(ctx, args), wait);
    };
  }

  // -----------------------------
  // Service Combo
  // -----------------------------
  (function initServiceCombo() {
    const combo = document.getElementById('serviceCombo');
    const menu = document.getElementById('serviceMenu');
    const select = document.getElementById('serviceSelect');
    if (!combo || !menu || !select || !window.bootstrap) return;

    const dd = bootstrap.Dropdown.getOrCreateInstance(combo, { autoClose: true });

    const all = Array.from(select.options)
      .filter(o => (o.value || '').trim() !== '')
      .map(o => ({ value: o.value, text: o.text }));

    function render(list) {
      menu.innerHTML = '';
      if (!list.length) {
        const li = document.createElement('li');
        li.innerHTML = '<span class="dropdown-item-text text-muted">No results</span>';
        menu.appendChild(li);
        return;
      }

      list.forEach(item => {
        const li = document.createElement('li');
        const a = document.createElement('button');
        a.type = 'button';
        a.className = 'dropdown-item';
        a.textContent = item.text;
        a.dataset.value = item.value;

        a.addEventListener('click', () => {
          combo.value = item.text;
          select.value = item.value;
          select.dispatchEvent(new Event('change', { bubbles: true }));
          dd.hide();
        });

        li.appendChild(a);
        menu.appendChild(li);
      });
    }

    function filterAndShow() {
      const q = (combo.value || '').trim().toLowerCase();
      const filtered = !q ? all : all.filter(x => (x.text || '').toLowerCase().includes(q));
      render(filtered);
      dd.show();
    }

    if (select.value) {
      const cur = all.find(x => x.value === select.value);
      if (cur) combo.value = cur.text;
    }

    combo.addEventListener('focus', filterAndShow);
    combo.addEventListener('click', filterAndShow);
    combo.addEventListener('input', filterAndShow);

    combo.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        const first = menu.querySelector('.dropdown-item');
        if (first) first.click();
      }
      if (e.key === 'Escape') dd.hide();
    });

    combo.addEventListener('blur', () => {
      const t = (combo.value || '').trim();
      if (!t) {
        select.value = '';
        select.dispatchEvent(new Event('change', { bubbles: true }));
      }
    });
  })();

  // -----------------------------
  // Pricing Rules
  // -----------------------------
  (function initRules() {
    const serviceSelect = document.getElementById('serviceSelect');
    const rulesBlock = document.getElementById('rulesBlock');
    const ruleCombo = document.getElementById('ruleCombo');
    const ruleMenu = document.getElementById('ruleMenu');
    const selectedRulesWrap = document.getElementById('selectedRules');
    const hiddenInputsWrap = document.getElementById('ruleHiddenInputs');

    if (!serviceSelect || !rulesBlock || !ruleCombo || !ruleMenu || !window.bootstrap) return;

    const ruleDd = bootstrap.Dropdown.getOrCreateInstance(ruleCombo, { autoClose: true });

    let allRules = [];
    const selected = new Map();

    function notifyRulesChanged() {
      dispatchAppEvent('quote-item-rules-changed');
    }

    function clearSelected() {
      selected.clear();
      selectedRulesWrap.innerHTML = '';
      hiddenInputsWrap.innerHTML = '';
      notifyRulesChanged();
    }

    function addHidden(id) {
      const input = document.createElement('input');
      input.type = 'hidden';
      input.name = 'Form.Item.PricingRuleIds';
      input.value = id;
      input.dataset.ruleId = id;
      hiddenInputsWrap.appendChild(input);
    }

    function removeHidden(id) {
      const el = hiddenInputsWrap.querySelector(`input[data-rule-id="${id}"]`);
      if (el) el.remove();
    }

    function renderSelected() {
      selectedRulesWrap.innerHTML = '';

      for (const r of selected.values()) {
        // A removable chip rather than a status, but it still takes the theme's
        // badge colours so it matches everything else on the page.
        const badge = document.createElement('span');
        badge.className =
          'badge bg-primary-50 text-primary-600 border border-primary-600 d-inline-flex align-items-center gap-2';
        badge.style.padding = '8px 10px';
        badge.innerHTML = `
          <span>${escapeHtml(r.name)}</span>
          <button type="button" class="btn btn-sm p-0 border-0 text-primary" aria-label="Remove">
            <i class="ri-close-line" style="font-size:18px; line-height:1;"></i>
          </button>
        `;

        badge.querySelector('button').addEventListener('click', () => {
          selected.delete(r.id);
          removeHidden(r.id);
          renderSelected();
          notifyRulesChanged();
        });

        selectedRulesWrap.appendChild(badge);
      }
    }

    function renderMenu(list) {
      ruleMenu.innerHTML = '';
      if (!list.length) {
        const li = document.createElement('li');
        li.innerHTML = '<span class="dropdown-item-text text-muted">No rules</span>';
        ruleMenu.appendChild(li);
        return;
      }

      list.forEach(r => {
        const li = document.createElement('li');
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'dropdown-item';
        btn.textContent = `${r.name}${r.label ? ' — ' + r.label : ''}`;
        btn.addEventListener('click', () => {
          if (!selected.has(r.id)) {
            selected.set(r.id, r);
            addHidden(r.id);
            renderSelected();
            notifyRulesChanged();
          }
          ruleCombo.value = '';
          ruleDd.hide();
        });
        li.appendChild(btn);
        ruleMenu.appendChild(li);
      });
    }

    function filterAndShowRules() {
      const q = (ruleCombo.value || '').trim().toLowerCase();
      const filtered = !q ? allRules : allRules.filter(x => (x.name || '').toLowerCase().includes(q));
      renderMenu(filtered);
      ruleDd.show();
    }

    async function loadRulesForService(serviceId) {
      clearSelected();

      if (!serviceId) {
        rulesBlock.style.display = 'none';
        allRules = [];
        return;
      }

      const url = `?handler=PricingRules&serviceId=${encodeURIComponent(serviceId)}`;
      const res = await fetch(url, { headers: { 'Accept': 'application/json' } });
      const data = await res.json();

      allRules = Array.isArray(data) ? data : [];
      if (!allRules.length) {
        rulesBlock.style.display = 'none';
        notifyRulesChanged();
        return;
      }

      rulesBlock.style.display = '';
      ruleCombo.value = '';
      renderMenu(allRules);
      notifyRulesChanged();
    }

    ruleCombo.addEventListener('focus', filterAndShowRules);
    ruleCombo.addEventListener('click', filterAndShowRules);
    ruleCombo.addEventListener('input', filterAndShowRules);

    ruleCombo.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        const first = ruleMenu.querySelector('.dropdown-item');
        if (first) first.click();
      }
      if (e.key === 'Escape') ruleDd.hide();
    });

    window.__loadRulesForService = loadRulesForService;

    if (serviceSelect.value) loadRulesForService(serviceSelect.value);
  })();

  // -----------------------------
  // Config Schema → Dynamic Form
  // -----------------------------
  (function initConfigSchemaForm() {
    const serviceSelect = document.getElementById('serviceSelect');
    const configBlock = document.getElementById('configBlock');
    const cfgFields = document.getElementById('cfgFields');
    const cfgEmpty = document.getElementById('cfgEmpty');
    const cfgLoading = document.getElementById('cfgLoading');
    const resetBtn = document.getElementById('cfgResetBtn');
    const formatBtn = document.getElementById('cfgFormatBtn');
    const cfgJson = document.getElementById('cfg-json');
    const cfgJsonHidden = document.getElementById('cfg-json-hidden');
    const btnSubmit = document.getElementById('btnSubmit');

    if (!serviceSelect || !configBlock || !cfgFields || !cfgEmpty || !cfgJson || !cfgJsonHidden) return;

    let schema = null;
    let cfg = {};

    function syncJsonTargets(pretty) {
      const text = stringifyJson(cfg, pretty);
      cfgJson.value = text;
      cfgJsonHidden.value = text;
      dispatchAppEvent('quote-item-config-changed');
    }

    function normalizeCfgBySchema() {
      if (!schema || schema.type !== 'object' || !schema.properties) return;

      if (schema.additionalProperties === false) {
        const allowed = new Set(Object.keys(schema.properties));
        Object.keys(cfg).forEach(k => {
          if (!allowed.has(k)) delete cfg[k];
        });
      }

      Object.entries(schema.properties).forEach(([key, def]) => {
        if (cfg[key] === undefined && def && typeof def === 'object' && def.default !== undefined) {
          cfg[key] = def.default;
        }
      });
    }

    function isRequired(key) {
      return !!(schema && Array.isArray(schema.required) &&
        schema.required.some(x => (x || '').toLowerCase() === key.toLowerCase()));
    }

    function inputId(key) { return `cfg_${key}`; }

    function setControlValue(def, control, val) {
      const hasEnum = Array.isArray(def.enum);
      const t = (def.type || (hasEnum ? 'string' : 'string'));

      if (t === 'boolean') {
        control.checked = !!val;
        return;
      }

      if (val === undefined || val === null) {
        control.value = '';
        return;
      }

      control.value = String(val);
    }

    function writeValueFromControl(key, def, control) {
      const hasEnum = Array.isArray(def.enum);
      const t = (def.type || (hasEnum ? 'string' : 'string'));

      if (t === 'boolean') {
        cfg[key] = !!control.checked;
        return;
      }

      if (control.value === '' || control.value === null || control.value === undefined) {
        delete cfg[key];
        return;
      }

      if (hasEnum) {
        cfg[key] = control.value;
        return;
      }

      if (t === 'integer') {
        const n = Number(control.value);
        if (!Number.isFinite(n)) {
          delete cfg[key];
          return;
        }
        cfg[key] = Math.trunc(n);
        return;
      }

      if (t === 'number') {
        const n = Number(control.value);
        if (!Number.isFinite(n)) {
          delete cfg[key];
          return;
        }
        cfg[key] = n;
        return;
      }

      cfg[key] = control.value;
    }

    function renderField(key, def) {
      const col = document.createElement('div');
      col.className = 'col-12 col-md-6';

      const title = def.title || def.label || key;
      const desc = def.description || '';
      const required = isRequired(key);

      const wrapper = document.createElement('div');

      const label = document.createElement('label');
      label.className = 'form-label';
      label.setAttribute('for', inputId(key));
      label.innerHTML = `${escapeHtml(title)}${required ? ' <span class="text-danger">*</span>' : ''}`;
      wrapper.appendChild(label);

      const hasEnum = Array.isArray(def.enum);
      const t = (def.type || (hasEnum ? 'string' : 'string'));

      let control;

      if (t === 'boolean') {
        const div = document.createElement('div');
        div.className = 'form-check form-switch';

        control = document.createElement('input');
        control.type = 'checkbox';
        control.className = 'form-check-input';
        control.id = inputId(key);

        const lab2 = document.createElement('label');
        lab2.className = 'form-check-label';
        lab2.setAttribute('for', inputId(key));
        lab2.textContent = title;

        div.appendChild(control);
        div.appendChild(lab2);
        wrapper.appendChild(div);
      } else if (hasEnum) {
        control = document.createElement('select');
        control.className = 'form-select';
        control.id = inputId(key);
        if (required) control.required = true;

        const optEmpty = document.createElement('option');
        optEmpty.value = '';
        optEmpty.textContent = '-- select --';
        control.appendChild(optEmpty);

        def.enum.forEach(v => {
          const opt = document.createElement('option');
          opt.value = String(v);
          opt.textContent = String(v);
          control.appendChild(opt);
        });

        wrapper.appendChild(control);
      } else if (t === 'integer' || t === 'number') {
        control = document.createElement('input');
        control.type = 'number';
        control.className = 'form-control';
        control.id = inputId(key);
        if (required) control.required = true;

        if (t === 'integer') control.step = '1';
        if (typeof def.minimum === 'number') control.min = String(def.minimum);
        if (typeof def.maximum === 'number') control.max = String(def.maximum);

        wrapper.appendChild(control);
      } else {
        control = document.createElement('input');
        control.type = 'text';
        control.className = 'form-control';
        control.id = inputId(key);
        if (required) control.required = true;

        if (typeof def.minLength === 'number') control.minLength = def.minLength;
        if (typeof def.maxLength === 'number') control.maxLength = def.maxLength;

        wrapper.appendChild(control);
      }

      if (desc) {
        const help = document.createElement('div');
        help.className = 'form-text';
        help.textContent = desc;
        wrapper.appendChild(help);
      }

      control.addEventListener('change', () => {
        writeValueFromControl(key, def, control);
        syncJsonTargets(false);
      });

      control.addEventListener('input', () => {
        writeValueFromControl(key, def, control);
        syncJsonTargets(false);
      });

      setControlValue(def, control, cfg[key]);
      col.appendChild(wrapper);
      return col;
    }

    function renderAll() {
      cfgFields.innerHTML = '';

      if (!schema || schema.type !== 'object' || !schema.properties || typeof schema.properties !== 'object') {
        cfgEmpty.classList.remove('d-none');
        dispatchAppEvent('quote-item-config-changed');
        return;
      }

      cfgEmpty.classList.add('d-none');

      Object.entries(schema.properties).forEach(([key, def]) => {
        if (!def || typeof def !== 'object') return;
        cfgFields.appendChild(renderField(key, def));
      });

      syncJsonTargets(false);
    }

    async function loadSchema(serviceId) {
      schema = null;
      cfgFields.innerHTML = '';
      cfgEmpty.classList.add('d-none');

      if (!serviceId) {
        configBlock.style.display = 'none';
        cfg = {};
        syncJsonTargets(false);
        return;
      }

      configBlock.style.display = '';

      const parsed = tryParseJson(cfgJson.value);
      cfg = parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : {};

      if (btnSubmit) btnSubmit.disabled = true;
      cfgLoading.classList.remove('d-none');

      const url = `?handler=ServiceSchema&serviceId=${encodeURIComponent(serviceId)}`;
      const res = await fetch(url, { headers: { 'Accept': 'application/json' } });

      cfgLoading.classList.add('d-none');
      if (btnSubmit) btnSubmit.disabled = false;

      if (!res.ok) {
        schema = null;
        cfgEmpty.classList.remove('d-none');
        renderAll();
        return;
      }

      const txt = (await res.text()).trim();
      schema = txt ? tryParseJson(txt) : null;

      if (!schema) {
        cfgEmpty.classList.remove('d-none');
        renderAll();
        return;
      }

      normalizeCfgBySchema();
      renderAll();
    }

    resetBtn?.addEventListener('click', () => {
      cfg = {};
      normalizeCfgBySchema();
      renderAll();
    });

    formatBtn?.addEventListener('click', () => {
      const p = tryParseJson(cfgJson.value);
      if (!p) {
        alert('Invalid JSON.');
        return;
      }
      cfg = p;
      normalizeCfgBySchema();
      syncJsonTargets(true);
    });

    serviceSelect.addEventListener('change', async () => {
      const serviceId = serviceSelect.value;

      if (window.__loadRulesForService) {
        await window.__loadRulesForService(serviceId);
      }

      await loadSchema(serviceId);
      dispatchAppEvent('quote-item-service-changed');
    });

    if (serviceSelect.value) {
      loadSchema(serviceSelect.value);
      if (window.__loadRulesForService) window.__loadRulesForService(serviceSelect.value);
    } else {
      configBlock.style.display = 'none';
      cfg = {};
      syncJsonTargets(false);
    }
  })();

  // -----------------------------
  // Live Price Preview Logic
  // -----------------------------
  (function initLivePricePreview() {
    const form = document.querySelector('form[method="post"]');
    const serviceSelect = document.getElementById('serviceSelect');
    const quantityInput = document.getElementById('Form_Item_Quantity');
    const billingCycleSelect = document.getElementById('Form_Item_BillingCycle');
    const discountTypeSelect = document.getElementById('Form_Item_DiscountType');
    const discountValueInput = document.getElementById('Form_Item_DiscountValue');
    const unitNameInput = document.getElementById('Form_Item_UnitName');
    const descriptionInput = document.getElementById('Form_Item_Description');
    const cfgJsonHidden = document.getElementById('cfg-json-hidden');
    const previewCard = document.getElementById('livePriceCard');
    const previewEmpty = document.getElementById('livePriceEmpty');
    const previewContent = document.getElementById('livePriceContent');
    const previewStatus = document.getElementById('livePriceStatus');
    const previewBaseUnit = document.getElementById('previewBaseUnit');
    const previewEffectiveUnit = document.getElementById('previewEffectiveUnit');
    const previewDiscount = document.getElementById('previewDiscount');
    const previewTotal = document.getElementById('previewTotal');
    const previewRules = document.getElementById('previewRules');
    const previewRulesEmpty = document.getElementById('previewRulesEmpty');

    if (!form || !serviceSelect || !previewCard || !previewEmpty || !previewContent) return;

    const currency = previewCard.dataset.currency || 'EUR';
    let controller = null;

    function formatMoney(value) {
      const number = Number(value || 0);
      try {
        return new Intl.NumberFormat(undefined, {
          style: 'currency', currency, minimumFractionDigits: 2
        }).format(number);
      } catch { return `${number.toFixed(2)} ${currency}`; }
    }

    function getSelectedRuleIds() {
      return Array.from(document.querySelectorAll('input[name="Form.Item.PricingRuleIds"]'))
        .map(x => x.value).filter(Boolean);
    }

 async function fetchPreview() {
        const serviceId = serviceSelect.value;
        if (!serviceId) {
            previewCard.style.display = 'none';
            return;
        }

        if (controller) controller.abort();
        controller = new AbortController();

        const rawDiscountValue = (discountValueInput?.value || '').trim();

        // بناء الـ Payload ليتوافق مع الـ Request Model الجديد في C#
        const payload = {
            serviceId: serviceId,
            quantity: parseFloat(quantityInput?.value || '1') || 1,
            billingCycle: billingCycleSelect?.value || 'OneTime',
            discountType: discountTypeSelect?.value || null,
            discountValue: rawDiscountValue === '' ? null : parseFloat(rawDiscountValue),
            configJson: cfgJsonHidden?.value || "{}",
            pricingRuleIds: getSelectedRuleIds()
        };

        try {
            const res = await fetch('?handler=PreviewPrice', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': form.querySelector('input[name="__RequestVerificationToken"]').value
                },
                body: JSON.stringify(payload),
                signal: controller.signal
            });

            const data = await res.json().catch(() => null);

            if (!res.ok || !data?.ok) {
                previewCard.style.display = '';
                previewEmpty.classList.remove('d-none');
                previewContent.classList.add('d-none');
                previewEmpty.textContent = data?.message || 'Unable to calculate live price preview.';
                console.error('Preview error:', data);
                return;
            }

            // استخراج البيانات من الـ Breakdown المرتجع من السيرفر
            const breakdown = data.breakdown || {};
            const rules = Array.isArray(breakdown.pricingRules) ? breakdown.pricingRules : [];

            const discountFromField = Number(breakdown?.discount?.fromField || 0);
            const discountFromRules = Number(breakdown?.discount?.fromRules || 0);
            const discountAmount = Number(
                breakdown?.discount?.amount ?? (discountFromField + discountFromRules)
            );

            // تحديث عناصر الواجهة
            previewCard.style.display = '';
            previewEmpty.classList.add('d-none');
            previewContent.classList.remove('d-none');

            if (previewStatus) {
                previewStatus.textContent = data.serviceName || 'Live price calculated';
            }

            previewBaseUnit.textContent = formatMoney(Number(breakdown.baseUnitPrice || 0));
            previewEffectiveUnit.textContent = formatMoney(Number(data.effectiveUnitPrice ?? breakdown.unitPrice ?? 0));
            previewDiscount.textContent = discountAmount > 0 ? formatMoney(discountAmount) : '—';
            previewTotal.textContent = formatMoney(Number(breakdown.total || 0));

            // عرض القواعد المطبقة (Pricing Rules) بشكل ديناميكي
            if (previewRules && previewRulesEmpty) {
                previewRules.innerHTML = '';

                if (!rules.length) {
                    previewRulesEmpty.classList.remove('d-none');
                } else {
                    previewRulesEmpty.classList.add('d-none');

                    rules.forEach(rule => {
                        const beforeTotal = Number(rule.beforeTotal || 0);
                        const afterTotal = Number(rule.afterTotal || 0);
                        const discountApplied = Number(rule.discountApplied || 0);
                        const delta = afterTotal - beforeTotal;

                        let effectText = 'Applied';
                        if (discountApplied > 0) {
                            effectText = `- ${formatMoney(discountApplied)}`;
                        } else if (Math.abs(delta) > 0.0001) {
                            effectText = `${delta > 0 ? '+' : '-'} ${formatMoney(Math.abs(delta))}`;
                        } else if (rule.afterUnitPrice !== undefined && rule.afterUnitPrice !== null) {
                            effectText = `${formatMoney(Number(rule.afterUnitPrice))} / unit`;
                        }

                        const row = document.createElement('div');
                        row.className = 'd-flex align-items-center justify-content-between gap-3 border rounded px-3 py-2';
                        row.innerHTML = `
            <div>
              <div class="fw-semibold">${rule.name || 'Rule'}</div>
              <div class="small text-muted">${rule.action || 'Applied'}</div>
            </div>
            <div class="fw-semibold text-nowrap">${effectText}</div>
          `;
                        previewRules.appendChild(row);
                    });
                }
            }
        } catch (e) {
            if (e.name !== 'AbortError') {
                console.error('Live preview error:', e);
            }
        }
    }

    const debouncedFetch = debounce(fetchPreview, 400);

    [quantityInput, billingCycleSelect, discountTypeSelect, discountValueInput].forEach(el => {
      el?.addEventListener('change', debouncedFetch);
      el?.addEventListener('input', debouncedFetch);
    });

    document.addEventListener('quote-item-rules-changed', debouncedFetch);
    document.addEventListener('quote-item-config-changed', debouncedFetch);
    document.addEventListener('quote-item-service-changed', fetchPreview);
  })();
})();
