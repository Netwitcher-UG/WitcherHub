(function () {
  // Helpers
  function escapeHtml(s) { return (s || '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
  function tryParseJson(raw) { try { return JSON.parse(raw || "{}"); } catch { return null; } }
  function stringifyJson(obj, pretty) { return JSON.stringify(obj ?? {}, null, pretty ? 2 : 0); }
  function dispatchAppEvent(name) { document.dispatchEvent(new CustomEvent(name)); }
  function debounce(fn, wait) { let t = null; return function () { clearTimeout(t); t = setTimeout(() => fn.apply(this, arguments), wait); }; }

  // 1. Service Combo & 2. Pricing Rules & 3. Config Schema (أكوادك الحالية مدمجة هنا مع تفعيل الـ Events)
  // [ملاحظة: تأكد من إضافة dispatchAppEvent('contract-item-rules-changed') عند تغيير القواعد]
  // [إضافة dispatchAppEvent('contract-item-config-changed') عند تغيير الـ JSON]

  // 4. Live Price Preview Logic
  (function initLivePricePreview() {
    const form = document.querySelector('form[method="post"]');
    const serviceSelect = document.getElementById('serviceSelect');
    const quantityInput = document.getElementById('Form_Item_Quantity');
    const billingCycleSelect = document.getElementById('Form_Item_BillingCycle');
    const discountTypeSelect = document.getElementById('Form_Item_DiscountType');
    const discountValueInput = document.getElementById('Form_Item_DiscountValue');
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

    if (!form || !serviceSelect || !previewCard) return;

    const currency = previewCard.dataset.currency || 'EUR';
    let controller = null;

    function formatMoney(value) {
      return new Intl.NumberFormat(undefined, { style: 'currency', currency, minimumFractionDigits: 2 }).format(Number(value || 0));
    }

    function getSelectedRuleIds() {
      return Array.from(document.querySelectorAll('input[name="Form.Item.PricingRuleIds"]')).map(x => x.value).filter(Boolean);
    }

    async function fetchPreview() {
      const serviceId = serviceSelect.value;
      if (!serviceId) { previewCard.style.display = 'none'; return; }

      if (controller) controller.abort();
      controller = new AbortController();

      const payload = {
        serviceId: serviceId,
        quantity: parseFloat(quantityInput?.value || '1') || 1,
        billingCycle: billingCycleSelect?.value || 'OneTime',
        discountType: discountTypeSelect?.value || null,
        discountValue: parseFloat(discountValueInput?.value) || null,
        configJson: cfgJsonHidden?.value || "{}",
        pricingRuleIds: getSelectedRuleIds()
      };

      try {
        const res = await fetch('?handler=PreviewPrice', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': form.querySelector('input[name="__RequestVerificationToken"]').value },
          body: JSON.stringify(payload),
          signal: controller.signal
        });

        const data = await res.json();
        if (!data.ok) return;

        const breakdown = data.breakdown || {};
        previewCard.style.display = '';
        previewEmpty.classList.add('d-none');
        previewContent.classList.remove('d-none');

        previewBaseUnit.textContent = formatMoney(breakdown.baseUnitPrice);
        previewEffectiveUnit.textContent = formatMoney(data.effectiveUnitPrice);
        previewDiscount.textContent = formatMoney(breakdown.discount?.amount || 0);
        previewTotal.textContent = formatMoney(breakdown.total);

        // Render Applied Rules
        previewRules.innerHTML = '';
        const rules = breakdown.pricingRules || [];
        if (rules.length === 0) previewRulesEmpty.classList.remove('d-none');
        else {
            previewRulesEmpty.classList.add('d-none');
            rules.forEach(r => {
                const row = document.createElement('div');
                row.className = 'd-flex justify-content-between border rounded p-2';
                row.innerHTML = `<div>${r.name}</div><div class="fw-bold">${formatMoney(r.discountApplied || 0)}</div>`;
                previewRules.appendChild(row);
            });
        }
      } catch (e) { if (e.name !== 'AbortError') console.error(e); }
    }

    const debouncedFetch = debounce(fetchPreview, 400);
    [quantityInput, billingCycleSelect, discountTypeSelect, discountValueInput].forEach(el => {
      el?.addEventListener('change', debouncedFetch);
      el?.addEventListener('input', debouncedFetch);
    });
    document.addEventListener('contract-item-rules-changed', debouncedFetch);
    document.addEventListener('contract-item-config-changed', debouncedFetch);
    document.addEventListener('contract-item-service-changed', fetchPreview);
  })();
})();
