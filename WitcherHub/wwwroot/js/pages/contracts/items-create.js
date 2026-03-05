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

  // -----------------------------
  // Service Combo (typeahead over hidden <select>)
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
        li.innerHTML = `<span class="dropdown-item-text text-muted">No results</span>`;
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

    // init selection (postback)
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
  // Pricing Rules (loads by serviceId)
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
    const selected = new Map(); // id -> rule

    function clearSelected() {
      selected.clear();
      selectedRulesWrap.innerHTML = '';
      hiddenInputsWrap.innerHTML = '';
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
        const badge = document.createElement('span');
        badge.className = 'badge bg-primary bg-opacity-10 text-primary d-inline-flex align-items-center gap-2';
        badge.style.padding = '8px 10px';
        badge.innerHTML = `
          <span>${escapeHtml(r.name)}</span>
          <button type="button" class="btn btn-sm p-0 border-0 text-primary" aria-label="Remove">
            <i class="material-icons-outlined" style="font-size:18px; line-height:1;">close</i>
          </button>
        `;
        badge.querySelector('button').addEventListener('click', () => {
          selected.delete(r.id);
          removeHidden(r.id);
          renderSelected();
        });
        selectedRulesWrap.appendChild(badge);
      }
    }

    function renderMenu(list) {
      ruleMenu.innerHTML = '';
      if (!list.length) {
        const li = document.createElement('li');
        li.innerHTML = `<span class="dropdown-item-text text-muted">No rules</span>`;
        ruleMenu.appendChild(li);
        return;
      }

      list.forEach(r => {
        const li = document.createElement('li');
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'dropdown-item';
        btn.textContent = `${r.name}${r.label ? " — " + r.label : ""}`;
        btn.addEventListener('click', () => {
          if (!selected.has(r.id)) {
            selected.set(r.id, r);
            addHidden(r.id);
            renderSelected();
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
        return;
      }

      rulesBlock.style.display = '';
      ruleCombo.value = '';
      renderMenu(allRules);
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

    // do not attach change here; we do it once in combined handler below
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
    }

    function normalizeCfgBySchema() {
      if (!schema || schema.type !== 'object' || !schema.properties) return;

      // drop unknown keys if additionalProperties === false
      if (schema.additionalProperties === false) {
        const allowed = new Set(Object.keys(schema.properties));
        Object.keys(cfg).forEach(k => {
          if (!allowed.has(k)) delete cfg[k];
        });
      }

      // apply defaults
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

      if (t === 'boolean') { control.checked = !!val; return; }
      if (val === undefined || val === null) { control.value = ''; return; }
      control.value = String(val);
    }

    function writeValueFromControl(key, def, control) {
      const hasEnum = Array.isArray(def.enum);
      const t = (def.type || (hasEnum ? 'string' : 'string'));

      if (t === 'boolean') { cfg[key] = !!control.checked; return; }

      if (control.value === '' || control.value === null || control.value === undefined) {
        delete cfg[key];
        return;
      }

      if (hasEnum) { cfg[key] = control.value; return; }
      if (t === 'integer') {
        const n = Number(control.value);
        if (!Number.isFinite(n)) { delete cfg[key]; return; }
        cfg[key] = Math.trunc(n);
        return;
      }
      if (t === 'number') {
        const n = Number(control.value);
        if (!Number.isFinite(n)) { delete cfg[key]; return; }
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
      }
      else if (hasEnum) {
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
      }
      else if (t === 'integer' || t === 'number') {
        control = document.createElement('input');
        control.type = 'number';
        control.className = 'form-control';
        control.id = inputId(key);
        if (required) control.required = true;

        if (t === 'integer') control.step = '1';
        if (typeof def.minimum === 'number') control.min = String(def.minimum);
        if (typeof def.maximum === 'number') control.max = String(def.maximum);

        wrapper.appendChild(control);
      }
      else {
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

      control.addEventListener('change', () => { writeValueFromControl(key, def, control); syncJsonTargets(false); });
      control.addEventListener('input', () => { writeValueFromControl(key, def, control); syncJsonTargets(false); });

      setControlValue(def, control, cfg[key]);
      col.appendChild(wrapper);
      return col;
    }

    function renderAll() {
      cfgFields.innerHTML = '';

      if (!schema || schema.type !== 'object' || !schema.properties || typeof schema.properties !== 'object') {
        cfgEmpty.classList.remove('d-none');
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

      // start from current json (postback safe)
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
        alert("Invalid JSON.");
        return;
      }
      cfg = p;
      normalizeCfgBySchema();
      syncJsonTargets(true);
    });

    // Combined service change:
    serviceSelect.addEventListener('change', async () => {
      const serviceId = serviceSelect.value;

      // load rules
      if (window.__loadRulesForService) {
        window.__loadRulesForService(serviceId);
      }

      // load schema + render config
      await loadSchema(serviceId);
    });

    // initial (postback)
    if (serviceSelect.value) {
      loadSchema(serviceSelect.value);
      if (window.__loadRulesForService) window.__loadRulesForService(serviceSelect.value);
    } else {
      configBlock.style.display = 'none';
      cfg = {};
      syncJsonTargets(false);
    }

  })();

})();
