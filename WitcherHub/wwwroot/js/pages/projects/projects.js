(function () {
    'use strict';

    function $(id) {
        return document.getElementById(id);
    }
    function saveSearchReloadState(key, input) {
        if (!input) return;

        sessionStorage.setItem(key, JSON.stringify({
            hadFocus: document.activeElement === input,
            start: typeof input.selectionStart === 'number' ? input.selectionStart : null,
            end: typeof input.selectionEnd === 'number' ? input.selectionEnd : null
        }));
    }

    function restoreOrBlurSearchState(key, input) {
        if (!input) return;

        const raw = sessionStorage.getItem(key);
        if (raw) {
            sessionStorage.removeItem(key);

            try {
                const state = JSON.parse(raw);
                if (state?.hadFocus) {
                    requestAnimationFrame(function () {
                        input.focus({ preventScroll: true });

                        const len = input.value.length;
                        const start = Math.min(state.start ?? len, len);
                        const end = Math.min(state.end ?? len, len);

                        if (typeof input.setSelectionRange === 'function') {
                            input.setSelectionRange(start, end);
                        }
                    });
                    return;
                }
            } catch { }
        }

        if (!input.value.trim()) return;

        requestAnimationFrame(function () {
            if (document.activeElement === input) {
                input.blur();
            }
        });
    }

    function initSearchClearButtons(root = document) {
        root.querySelectorAll('.order-search').forEach(function (form) {
            if (form.dataset.clearInit === '1') return;
            form.dataset.clearInit = '1';

            const input = form.querySelector('input[type="text"][name="q"]');
            const clearBtn = form.querySelector('[data-search-clear]');
            if (!input || !clearBtn) return;

            function syncClearButton() {
                clearBtn.classList.toggle('d-none', !input.value.trim());
            }

            clearBtn.addEventListener('click', function () {
                input.value = '';
                syncClearButton();

                const pageInput = form.querySelector('input[name="p"]');
                if (pageInput) pageInput.value = '1';

                if (typeof form.requestSubmit === 'function') {
                    form.requestSubmit();
                } else {
                    form.submit();
                }
            });

            input.addEventListener('input', syncClearButton);
            syncClearButton();
        });
    }
    function removeSearchFocusOnLoad(tableCardId) {
        const host = document.getElementById(tableCardId);
        const input = host?.querySelector('.order-search input[name="q"]');
        if (!input) return;
        if (!input.value.trim()) return;

        requestAnimationFrame(function () {
            if (document.activeElement === input) {
                input.blur();
            }
        });
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

        modalEl.addEventListener('hidden.bs.modal', function () {
            customerTouched = false;
            setCustomerError('');
        });

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

            form.dataset.submitting = '0';

            form.querySelectorAll('input, textarea, select').forEach(function (el) {
                const tag = (el.tagName || '').toLowerCase();
                const type = (el.getAttribute('type') || '').toLowerCase();

                if (type === 'hidden') return;
                if (type === 'submit' || type === 'button') return;

                if (tag === 'select') {
                    if (el.options.length > 0) {
                        el.selectedIndex = 0;
                    } else {
                        el.value = '';
                    }
                    return;
                }

                if (type === 'checkbox' || type === 'radio') {
                    el.checked = false;
                    return;
                }

                el.value = '';
            });

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
                const unobtrusive = $form.data('unobtrusiveValidation');

                if (validator && typeof validator.resetForm === 'function') {
                    validator.resetForm();
                }

                if (unobtrusive && unobtrusive.options) {
                    $form.find('.input-validation-error').removeClass('input-validation-error');
                    $form.find('[data-valmsg-for]').removeClass('field-validation-error').addClass('field-validation-valid').empty();
                }
            }
        });
    })();
    (function bindProjectsLiveSearch() {
        let debounceTimer = null;
        let activeController = null;

        function initProjectsLiveSearch() {
            const host = $('projectsTableCard');
            if (!host) return;

            const form = host.querySelector('.order-search');
            const input = form?.querySelector('input[name="q"]');
            if (!form || !input) return;
            initSearchClearButtons(host);
            if (form.dataset.liveSearchBound === '1') return;
            form.dataset.liveSearchBound = '1';

            async function reloadProjectsTable() {
                const currentHost = $('projectsTableCard');
                if (!currentHost) return;

                const currentForm = currentHost.querySelector('.order-search');
                const currentInput = currentForm?.querySelector('input[name="q"]');
                if (!currentForm || !currentInput) return;

                const searchState = captureSearchState(currentInput);

                const url = new URL(window.location.href);
                const q = currentInput.value.trim();

                if (q) url.searchParams.set('q', q);
                else url.searchParams.delete('q');

                url.searchParams.set('p', '1');

                const pageSizeInput = currentForm.querySelector('input[name="pageSize"]');
                if (pageSizeInput?.value) {
                    url.searchParams.set('pageSize', pageSizeInput.value);
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
                    const newHost = doc.getElementById('projectsTableCard');
                    if (!newHost) return;

                    currentHost.outerHTML = newHost.outerHTML;

                    window.history.replaceState({}, '', url.pathname + url.search);

                    initProjectsLiveSearch();
                    restoreSearchState('projectsTableCard', searchState);
                } catch (err) {
                    if (err.name === 'AbortError') return;
                    console.error('Projects live search failed:', err);
                }
            }

            input.addEventListener('input', function () {
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(reloadProjectsTable, 500);
            });

            form.addEventListener('submit', function (e) {
                e.preventDefault();
                clearTimeout(debounceTimer);
                reloadProjectsTable();
            });
        }

        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', initProjectsLiveSearch);
        } else {
            initProjectsLiveSearch();
        }
    })();
    document.addEventListener('DOMContentLoaded', function () {
        initSearchClearButtons(document);
        removeSearchFocusOnLoad('projectsTableCard');
    });
})();