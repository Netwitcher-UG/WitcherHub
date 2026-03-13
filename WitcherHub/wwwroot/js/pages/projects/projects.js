(function () {
    'use strict';

    function $(id) {
        return document.getElementById(id);
    }

    function goToProjectWorkspace(projectId, tab) {
        const baseUrl = $('vcProjectWorkspaceUrl')?.value || '/Projects/Workspace';
        const url = new URL(baseUrl, window.location.origin);

        url.searchParams.set('id', projectId);
        url.searchParams.set('tab', tab || 'overview');

        window.location.href = url.pathname + url.search;
    }

    document.addEventListener('click', function (e) {
        if (e.target.closest('.vc-actions-wrap')) return;
        if (e.target.closest('button, a, input, textarea, select, label')) return;

        const tr = e.target.closest('tr');
        if (!tr) return;

        const pidEl = tr.querySelector('[data-project-id]');
        const pid = pidEl?.getAttribute('data-project-id') || '';
        if (!pid) return;

        goToProjectWorkspace(pid, 'overview');
    });

    (function bindProjectDeleteModal() {
        const modalEl = $('DeleteProjectConfirmModal');
        const titleEl = $('DeleteProjectConfirmModalLabel');
        const messageEl = $('DeleteProjectConfirmModalMessage');
        const confirmBtn = $('DeleteProjectConfirmModalSubmit');
        const hid = $('tblDeleteProjectId');
        const form = $('tblDeleteForm');

        if (!modalEl || !titleEl || !messageEl || !confirmBtn || !hid || !form) return;
        if (!window.bootstrap || !window.bootstrap.Modal) return;

        const modal = window.bootstrap.Modal.getOrCreateInstance(modalEl);
        let pendingProjectId = null;

        document.addEventListener('click', function (e) {
            const btn = e.target.closest('[data-vc-action="table-delete-project"]');
            if (!btn) return;

            e.preventDefault();

            pendingProjectId = btn.getAttribute('data-project-id');
            if (!pendingProjectId) return;

            titleEl.textContent = 'Delete project';
            messageEl.textContent = 'Are you sure you want to delete this project?';

            modal.show();
        });

        confirmBtn.addEventListener('click', function () {
            if (!pendingProjectId) return;

            hid.value = pendingProjectId;
            pendingProjectId = null;

            modal.hide();
            form.submit();
        });

        modalEl.addEventListener('hidden.bs.modal', function () {
            pendingProjectId = null;
        });
    })();

    (function bindCreateProjectSubmitLoading() {
        const modalEl = $('FormModal');
        if (!modalEl) return;

        const form = modalEl.querySelector('form');
        if (!form) return;

        const submitBtn = form.querySelector('button[type="submit"], input[type="submit"]');
        if (!submitBtn) return;

        const customerSelect =
            form.querySelector('#Project_CustomerId') ||
            form.querySelector('select[name="Project.CustomerId"]');

        const customerErrorId =
            customerSelect?.getAttribute('aria-describedby') ||
            'Project_CustomerId-error';

        let customerError =
            (customerErrorId ? modalEl.querySelector(`#${customerErrorId}`) : null) ||
            modalEl.querySelector('[data-valmsg-for="Project.CustomerId"]') ||
            (customerErrorId ? form.querySelector(`#${customerErrorId}`) : null) ||
            form.querySelector('[data-valmsg-for="Project.CustomerId"]');

        if (!customerError && customerSelect) {
            customerError = document.createElement('span');
            customerError.id = customerErrorId;
            customerError.className = 'text-danger small field-validation-error';
            customerSelect.insertAdjacentElement('afterend', customerError);
        }

        const emptyGuid = '00000000-0000-0000-0000-000000000000';

        function setCustomerError(message) {
            if (customerError) {
                customerError.textContent = message || '';
                customerError.classList.remove('field-validation-valid', 'field-validation-error');
                customerError.classList.add(message ? 'field-validation-error' : 'field-validation-valid');
                customerError.style.display = message ? 'block' : 'none';
            }

            if (customerSelect) {
                customerSelect.classList.toggle('input-validation-error', !!message);

                if (message) {
                    customerSelect.setAttribute('aria-invalid', 'true');
                } else {
                    customerSelect.removeAttribute('aria-invalid');
                }
            }
        }

        function validateCustomer(showMessage) {
            if (!customerSelect) return true;

            const value = (customerSelect.value || '').trim();
            const ok = value !== '' && value !== emptyGuid;

            if (showMessage && !ok) {
                window.requestAnimationFrame(function () {
                    setCustomerError('Customer is required.');
                });
            } else {
                setCustomerError('');
            }

            return ok;
        }

        function startLoading() {
            if (form.dataset.submitting === '1') return;

            form.dataset.submitting = '1';
            submitBtn.disabled = true;

            if (!submitBtn.dataset.originalHtml) {
                submitBtn.dataset.originalHtml = submitBtn.innerHTML;
            }

            submitBtn.innerHTML = `
            <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
            Saving...
        `;
        }

        let customerTouched = false;

        if (customerSelect) {
            customerSelect.removeAttribute('required');

            customerSelect.addEventListener('change', function () {
                customerTouched = true;
                validateCustomer(true);
            });

            customerSelect.addEventListener('input', function () {
                if (customerTouched) validateCustomer(true);
            });

            customerSelect.addEventListener('blur', function () {
                customerTouched = true;
                validateCustomer(true);
            });

            setCustomerError('');
        }

        form.addEventListener('submit', function (e) {
            if (form.dataset.submitting === '1') {
                e.preventDefault();
                return;
            }

            const customerOk = validateCustomer(true);
            if (!customerOk) {
                e.preventDefault();
                customerTouched = true;
                return;
            }

            if (window.jQuery && typeof window.jQuery(form).valid === 'function') {
                const isValid = window.jQuery(form).valid();
                if (!isValid) {
                    e.preventDefault();
                    return;
                }
            }

            startLoading();
        });
    })();

    (function bindCreateProjectModalReset() {
        const modalEl = $('FormModal');
        if (!modalEl) return;

        modalEl.addEventListener('hidden.bs.modal', function () {
            const form = modalEl.querySelector('form');
            if (!form) return;

            form.reset();
            form.dataset.submitting = '0';

            const submitBtn = form.querySelector('button[type="submit"], input[type="submit"]');
            if (submitBtn) {
                submitBtn.disabled = false;

                if (submitBtn.dataset.originalHtml) {
                    submitBtn.innerHTML = submitBtn.dataset.originalHtml;
                }

                if (submitBtn.dataset.originalValue) {
                    submitBtn.value = submitBtn.dataset.originalValue;
                }
            }

            form.querySelectorAll('[data-valmsg-for]').forEach(function (el) {
                el.textContent = '';
                el.classList.remove('field-validation-error');
                el.classList.add('field-validation-valid');
                el.removeAttribute('style');
            });

            form.querySelectorAll('[data-valmsg-summary="true"]').forEach(function (el) {
                el.innerHTML = '';
                el.classList.remove('validation-summary-errors');
                el.classList.add('validation-summary-valid');
            });

            form.querySelectorAll('.input-validation-error').forEach(function (el) {
                el.classList.remove('input-validation-error');
            });

            form.querySelectorAll('[aria-invalid]').forEach(function (el) {
                el.removeAttribute('aria-invalid');
            });

            const customerSelect =
                form.querySelector('#Project_CustomerId') ||
                form.querySelector('select[name="Project.CustomerId"]');

            if (customerSelect) {
                customerSelect.removeAttribute('aria-invalid');
            }

            if (window.jQuery) {
                const $form = window.jQuery(form);
                const validator = $form.data('validator');
                if (validator && typeof validator.resetForm === 'function') {
                    validator.resetForm();
                }
            }
        });
    })();

})();