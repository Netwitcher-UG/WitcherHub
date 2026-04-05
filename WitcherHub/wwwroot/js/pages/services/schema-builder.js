// wwwroot/js/pages/services/schema-builder.js
(function() {
    "use strict";

    // ===== Templates جاهزة =====
    const SB_TEMPLATES = [
        {
            id: "none",
            name: "— Select template —",
            schema: null
        },
        {
            id: "website",
            name: "Website Design",
            schema: {
                type: "object",
                additionalProperties: false,
                required: ["pages", "languages"],
                properties: {
                    pages: { type: "integer", title: "Pages", minimum: 1, maximum: 50, default: 5 },
                    languages: { type: "integer", title: "Languages", minimum: 1, maximum: 10, default: 1 },
                    ecommerce: { type: "boolean", title: "E-commerce", default: false },
                    deadlineDays: { type: "integer", title: "Deadline (days)", minimum: 1, maximum: 90, default: 14 }
                }
            }
        },
        {
            id: "video",
            name: "Video Editing",
            schema: {
                type: "object",
                additionalProperties: false,
                required: ["minutes", "clips"],
                properties: {
                    minutes: { type: "number", title: "Minutes", minimum: 0, maximum: 300, default: 3 },
                    clips: { type: "integer", title: "Clips", minimum: 0, maximum: 2000, default: 30 },
                    revisionsIncluded: { type: "integer", title: "Revisions Included", minimum: 0, maximum: 10, default: 1 },
                    revisionsRequested: { type: "integer", title: "Revisions Requested", minimum: 0, maximum: 20, default: 1 },
                    urgencyHours: { type: "integer", title: "Urgency (hours)", minimum: 1, maximum: 240, default: 72 }
                }
            }
        },
        {
            id: "retainer",
            name: "Monthly Retainer",
            schema: {
                type: "object",
                additionalProperties: false,
                required: ["months", "hoursPerMonth"],
                properties: {
                    months: { type: "integer", title: "Months", minimum: 1, maximum: 24, default: 6 },
                    hoursPerMonth: { type: "number", title: "Hours/Month", minimum: 1, maximum: 80, default: 10 },
                    prioritySupport: { type: "boolean", title: "Priority Support", default: false }
                }
            }
        }
    ];

    function $(root, sel) { return root.querySelector(sel); }
    function $all(root, sel) { return Array.from(root.querySelectorAll(sel)); }

    function safeParseJson(text) {
        if (!text || !text.trim()) return { ok: true, value: null };
        try { return { ok: true, value: JSON.parse(text) }; }
        catch (e) { return { ok: false, error: e?.message || "Invalid JSON" }; }
    }

    function toBool(v) {
        if (typeof v === "boolean") return v;
        if (typeof v === "string") return v.toLowerCase() === "true";
        return false;
    }

    function normalizeKey(key) {
        return (key || "").trim();
    }

    function parseNumberOrNull(v) {
        if (v === null || v === undefined) return null;
        const s = String(v).trim();
        if (!s) return null;
        const n = Number(s);
        return Number.isFinite(n) ? n : null;
    }

    function parseDefaultByType(type, value) {
        if (value === null || value === undefined) return null;
        const s = String(value).trim();
        if (!s) return null;

        switch (type) {
            case "integer": {
                const n = Number(s);
                if (!Number.isFinite(n)) return null;
                return Math.trunc(n);
            }
            case "number": {
                const n = Number(s);
                return Number.isFinite(n) ? n : null;
            }
            case "boolean": {
                return s.toLowerCase() === "true";
            }
            default:
                return s;
        }
    }

    function schemaToFields(schema) {
        if (!schema || typeof schema !== "object") return [];

        const props = schema.properties || {};
        const req = new Set(schema.required || []);
        const fields = [];

        for (const key of Object.keys(props)) {
            const p = props[key] || {};
            const isSelect = Array.isArray(p.enum);
            const type = isSelect ? "select" : (p.type || "string");

            const f = {
                key,
                label: p.title || "",
                description: p.description || "",
                type: type,
                required: req.has(key),
                default: (p.default !== undefined && p.default !== null) ? String(p.default) : "",
                min: "",
                max: "",
                options: isSelect ? p.enum.join(",") : ""
            };

            if (type === "integer" || type === "number") {
                if (p.minimum !== undefined) f.min = String(p.minimum);
                if (p.maximum !== undefined) f.max = String(p.maximum);
            } else if (type === "string" || type === "select") {
                if (p.minLength !== undefined) f.min = String(p.minLength);
                if (p.maxLength !== undefined) f.max = String(p.maxLength);
            }

            fields.push(f);
        }

        return fields;
    }

    function fieldsToSchema(fields, allowAdditional) {
        const schema = {
            type: "object",
            additionalProperties: !!allowAdditional,
            properties: {}
        };

        const required = [];

        for (const f of fields) {
            const key = normalizeKey(f.key);
            if (!key) continue;

            const type = f.type || "string";
            const prop = {};

            // label/help
            if (f.label && f.label.trim()) prop.title = f.label.trim();
            if (f.description && f.description.trim()) prop.description = f.description.trim();

            // type / enum
            if (type === "select") {
                prop.type = "string";
                const opts = (f.options || "")
                    .split(",")
                    .map(x => x.trim())
                    .filter(x => x.length > 0);
                if (opts.length > 0) prop.enum = opts;
            } else {
                prop.type = type;
            }

            // constraints
            const min = parseNumberOrNull(f.min);
            const max = parseNumberOrNull(f.max);

            if (prop.type === "integer" || prop.type === "number") {
                if (min !== null) prop.minimum = min;
                if (max !== null) prop.maximum = max;
            } else if (prop.type === "string") {
                if (min !== null) prop.minLength = Math.trunc(min);
                if (max !== null) prop.maxLength = Math.trunc(max);
            }

            // default
            const def = parseDefaultByType(prop.type === "string" && prop.enum ? "string" : prop.type, f.default);
            if (def !== null) prop.default = def;

            schema.properties[key] = prop;

            if (toBool(f.required)) required.push(key);
        }

        if (required.length > 0) schema.required = required;

        return schema;
    }

    class SchemaBuilder {
        constructor(containerEl, textareaEl) {
            this.el = containerEl;
            this.textarea = textareaEl;
            this.fields = [];
            this.allowAdditional = false;

            this._renderShell();
            this._bind();
            this._initTemplates();
            this.loadFromTextarea(true);
        }

        _renderShell() {
            this.el.innerHTML = `
        <div class="d-flex flex-wrap gap-2 align-items-end mb-3">
          <div style="min-width: 240px" class="flex-grow-1">
            <label class="form-label text-light opacity-75 mb-1">Template</label>
            <select class="form-select form-select-sm" data-sb="template"></select>
          </div>

          <button type="button" class="btn btn-sm btn-outline-primary" data-sb="applyTemplate">
            Apply
          </button>

          <button type="button" class="btn btn-sm btn-outline-secondary" data-sb="loadJson">
            Load from JSON
          </button>

          <button type="button" class="btn btn-sm btn-outline-success" data-sb="updateJson">
            Update JSON
          </button>

          <button type="button" class="btn btn-sm btn-outline-danger" data-sb="clearAll">
            Clear
          </button>
        </div>

        <div class="form-check form-switch mb-2">
          <input class="form-check-input" type="checkbox" id="${this._uid("allowAdd")}" data-sb="allowAdditional">
          <label class="form-check-label text-light" for="${this._uid("allowAdd")}">
            Allow extra fields (additionalProperties)
          </label>
        </div>

        <div class="table-responsive">
          <table class="table table-sm table-dark table-hover align-middle mb-2">
            <thead>
              <tr>
                <th style="width: 14%">Key</th>
                <th style="width: 16%">Label</th>
                <th style="width: 12%">Type</th>
                <th style="width: 8%">Req</th>
                <th style="width: 14%">Default</th>
                <th style="width: 18%">Min / Max</th>
                <th style="width: 18%">Options (for Select)</th>
                <th style="width: 8%"></th>
              </tr>
            </thead>
            <tbody data-sb="tbody"></tbody>
          </table>
        </div>

        <button type="button" class="btn btn-sm btn-outline-info" data-sb="addField">
          + Add Field
        </button>

        <div class="small text-danger mt-2" data-sb="message"></div>
        <div class="small text-muted mt-1">
          Tip: Build here → it generates JSON Schema automatically into the JSON tab/textarea.
        </div>
      `;
        }

        _uid(prefix) {
            // generate stable-ish ids per container instance
            const base = this.el.getAttribute("id") || "sb";
            return `${prefix}-${base}`;
        }

        _bind() {
            const tbody = $(this.el, '[data-sb="tbody"]');
            const msg = $(this.el, '[data-sb="message"]');

            const updateMsg = (text) => { msg.textContent = text || ""; };

            // events
            this.el.addEventListener("click", (e) => {
                const btn = e.target.closest("[data-sb]");
                if (!btn) return;

                const action = btn.getAttribute("data-sb");

                if (action === "addField") {
                    this.fields.push({ key: "", label: "", type: "string", required: false, default: "", min: "", max: "", options: "", description: "" });
                    this.render();
                    this.updateTextarea();
                    updateMsg("");
                }

                if (action === "removeField") {
                    const idx = Number(btn.getAttribute("data-index"));
                    if (Number.isFinite(idx)) {
                        this.fields.splice(idx, 1);
                        this.render();
                        this.updateTextarea();
                        updateMsg("");
                    }
                }

                if (action === "updateJson") {
                    this.updateTextarea();
                    updateMsg("JSON updated.");
                }

                if (action === "loadJson") {
                    const ok = this.loadFromTextarea(false);
                    updateMsg(ok ? "Loaded from JSON." : "Couldn't load: invalid JSON schema.");
                }

                if (action === "clearAll") {
                    this.fields = [];
                    this.allowAdditional = false;
                    $(this.el, '[data-sb="allowAdditional"]').checked = false;
                    this.render();
                    this.textarea.value = "";
                    this.textarea.dispatchEvent(new Event("input", { bubbles: true }));
                    updateMsg("Cleared.");
                }

                if (action === "applyTemplate") {
                    const sel = $(this.el, '[data-sb="template"]');
                    const tpl = SB_TEMPLATES.find(x => x.id === sel.value);
                    if (!tpl || !tpl.schema) return;

                    this.applySchema(tpl.schema);
                    updateMsg(`Template applied: ${tpl.name}`);
                }
            });

            // change / input events for fields
            this.el.addEventListener("input", (e) => {
                const inp = e.target.closest("[data-sb-field]");
                if (!inp) return;

                const idx = Number(inp.getAttribute("data-index"));
                const prop = inp.getAttribute("data-sb-field");
                if (!Number.isFinite(idx) || !this.fields[idx]) return;

                this.fields[idx][prop] = inp.type === "checkbox" ? inp.checked : inp.value;

                // auto-update JSON
                this.updateTextarea();
            });

            this.el.addEventListener("change", (e) => {
                const allow = e.target.closest('[data-sb="allowAdditional"]');
                if (allow) {
                    this.allowAdditional = allow.checked;
                    this.updateTextarea();
                    return;
                }
            });

            // initial render
            this.render();
        }

        _initTemplates() {
            const sel = $(this.el, '[data-sb="template"]');
            sel.innerHTML = SB_TEMPLATES.map(t => `<option value="${t.id}">${t.name}</option>`).join("");
            sel.value = "none";
        }

        render() {
            const tbody = $(this.el, '[data-sb="tbody"]');
            tbody.innerHTML = "";

            this.fields.forEach((f, i) => {
                const tr = document.createElement("tr");
                tr.innerHTML = `
          <td>
            <input class="form-control form-control-sm" placeholder="e.g. pages"
                   data-sb-field="key" data-index="${i}" value="${escapeHtml(f.key || "")}">
          </td>
          <td>
            <input class="form-control form-control-sm" placeholder="Label"
                   data-sb-field="label" data-index="${i}" value="${escapeHtml(f.label || "")}">
          </td>
          <td>
            <select class="form-select form-select-sm"
                    data-sb-field="type" data-index="${i}">
              ${typeOptionsHtml(f.type)}
            </select>
          </td>
          <td class="text-center">
            <input type="checkbox" class="form-check-input"
                   data-sb-field="required" data-index="${i}" ${f.required ? "checked" : ""}>
          </td>
          <td>
            <input class="form-control form-control-sm" placeholder="Default"
                   data-sb-field="default" data-index="${i}" value="${escapeHtml(f.default || "")}">
          </td>
          <td>
            <div class="d-flex gap-1">
              <input class="form-control form-control-sm" placeholder="Min"
                     data-sb-field="min" data-index="${i}" value="${escapeHtml(f.min || "")}">
              <input class="form-control form-control-sm" placeholder="Max"
                     data-sb-field="max" data-index="${i}" value="${escapeHtml(f.max || "")}">
            </div>
          </td>
          <td>
            <input class="form-control form-control-sm" placeholder="a,b,c"
                   data-sb-field="options" data-index="${i}" value="${escapeHtml(f.options || "")}">
          </td>
          <td class="text-end">
            <button type="button" class="btn btn-sm btn-outline-danger"
                    data-sb="removeField" data-index="${i}">
              ✕
            </button>
          </td>
        `;
                tbody.appendChild(tr);
            });

            // update allowAdditional checkbox
            const chk = $(this.el, '[data-sb="allowAdditional"]');
            chk.checked = !!this.allowAdditional;
        }

        updateTextarea() {
            const schema = fieldsToSchema(this.fields, this.allowAdditional);

            // إذا ما في ولا property، خلّيها فاضية بدل JSON فاضي
            const hasProps = schema.properties && Object.keys(schema.properties).length > 0;
            if (!hasProps) {
                this.textarea.value = "";
                this.textarea.dispatchEvent(new Event("input", { bubbles: true }));
                return;
            }

            this.textarea.value = JSON.stringify(schema, null, 2);
            this.textarea.dispatchEvent(new Event("input", { bubbles: true }));
        }

        loadFromTextarea(silent) {
            const parsed = safeParseJson(this.textarea.value);
            if (!parsed.ok) return false;
            if (!parsed.value) {
                this.fields = [];
                this.allowAdditional = false;
                this.render();
                return true;
            }

            const s = parsed.value;
            if (!s || s.type !== "object") return false;

            this.allowAdditional = !!s.additionalProperties;
            this.fields = schemaToFields(s);
            this.render();
            if (!silent) this.updateTextarea(); // normalize formatting
            return true;
        }

        applySchema(schema) {
            this.allowAdditional = !!schema.additionalProperties;
            this.fields = schemaToFields(schema);
            this.render();
            this.updateTextarea();
        }
        reset() {
            this.fields = [];
            this.allowAdditional = false;

            const chk = this.el.querySelector('[data-sb="allowAdditional"]');
            if (chk) chk.checked = false;

            this.render();

            this.textarea.value = "";
            this.textarea.dispatchEvent(new Event("input", { bubbles: true }));
        }
    }

    function typeOptionsHtml(selected) {
        const types = [
            { v: "string", t: "Text" },
            { v: "integer", t: "Integer" },
            { v: "number", t: "Number" },
            { v: "boolean", t: "Boolean" },
            { v: "select", t: "Select" }
        ];
        return types.map(x => `<option value="${x.v}" ${x.v === selected ? "selected" : ""}>${x.t}</option>`).join("");
    }

    function escapeHtml(s) {
        return String(s)
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    // ===== Auto init for any container with data-schema-builder =====
    const INSTANCES = new Map();

    function initAll() {
        document.querySelectorAll("[data-schema-builder]").forEach(container => {
            const textareaSelector = container.getAttribute("data-schema-textarea");
            if (!textareaSelector) return;

            const textarea = document.querySelector(textareaSelector);
            if (!textarea) return;

            const inst = new SchemaBuilder(container, textarea);
            INSTANCES.set(container, inst);
        });

        // لما المستخدم يفتح تبويب الـ Builder، اعمل load من textarea (خصوصاً للـ View modal اللي بتتعبّى Ajax)
        document.addEventListener("shown.bs.tab", (e) => {
            const a = e.target;
            if (!a || !a.dataset) return;

            const containerSel = a.dataset.sbContainer;
            if (!containerSel) return;

            const container = document.querySelector(containerSel);
            if (!container) return;

            const inst = INSTANCES.get(container);
            if (!inst) return;

            inst.loadFromTextarea(true);
        });
    }
    document.addEventListener("schema-builder:reset", (e) => {
        const containerSel = e.detail?.container;
        if (!containerSel) return;

        const container = document.querySelector(containerSel);
        if (!container) return;

        const inst = INSTANCES.get(container);
        if (!inst) return;

        inst.reset();
    });
    document.addEventListener("DOMContentLoaded", initAll);
})();
