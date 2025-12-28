(function () {
    const UI = {};
    let modalInstance = null;

    // ---------- Helpers ----------
    function byId(id) { return document.getElementById(id); }

    function escapeHtml(str) {
        return String(str ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function ensureModal() {
        const el = byId('appModal');
        if (!el || !window.bootstrap?.Modal) return null;
        if (!modalInstance) modalInstance = new bootstrap.Modal(el, { backdrop: 'static' });
        return { el, instance: modalInstance };
    }

    function setModalContent({ title, bodyHtml, footerHtml, size }) {
        const modalEl = byId('appModal');
        const dialog = modalEl?.querySelector('.modal-dialog');
        if (dialog) {
            dialog.classList.remove('modal-sm', 'modal-lg', 'modal-xl');
            if (size) dialog.classList.add(size); // 'modal-sm' | 'modal-lg' | 'modal-xl'
        }

        const titleEl = byId('appModalTitle');
        const bodyEl = byId('appModalBody');
        const footerEl = byId('appModalFooter');

        if (titleEl) titleEl.textContent = title ?? '';
        if (bodyEl) bodyEl.innerHTML = bodyHtml ?? '';
        if (footerEl) footerEl.innerHTML = footerHtml ?? '';
    }

    // ---------- 1) Notifications / Toasts (Rounded Corners + Icon) ----------
    const notifyTypeMap = {
        success: { cls: 'bg-grd-success', icon: 'check_circle', text: 'text-white', close: 'text-white' },
        error: { cls: 'bg-grd-danger', icon: 'report_gmailerrorred', text: 'text-white', close: 'text-white' },
        warning: { cls: 'bg-grd-warning', icon: 'warning', text: 'text-dark', close: 'text-dark' },
        info: { cls: 'bg-grd-info', icon: 'info', text: 'text-white', close: 'text-white' },
        default: { cls: 'bg-grd-primary', icon: 'notifications', text: 'text-white', close: 'text-white' }
    };

    function ensureNotifyCss() {
        if (document.getElementById('uiNotifyCss')) return;
        const style = document.createElement('style');
        style.id = 'uiNotifyCss';
        style.textContent = `
/* Rounded Corners Notifications (Injected) */
.ui-notify-container{
  position: fixed;
  top: 90px;
  right: 20px;
  z-index: 3000;
  display: flex;
  flex-direction: column;
  gap: 12px;
  width: min(420px, calc(100vw - 40px));
}
.ui-notify{
  border-radius: 16px;
  overflow: hidden;
  box-shadow: 0 14px 40px rgba(0,0,0,.35);
  transform: translateY(-6px);
  opacity: 0;
  transition: all .18s ease;
}
.ui-notify.show{
  transform: translateY(0);
  opacity: 1;
}
.ui-notify-body{
  padding: 14px 16px;
  display: flex;
  align-items: flex-start;
  gap: 12px;
}
.ui-notify-icon{
  width: 40px;
  height: 40px;
  border-radius: 12px;
  display:flex;
  align-items:center;
  justify-content:center;
  background: rgba(255,255,255,.18);
  flex: 0 0 auto;
}
.ui-notify-title{
  font-weight: 700;
  margin: 0 0 2px 0;
  line-height: 1.2;
}
.ui-notify-text{
  margin: 0;
  opacity: .95;
  line-height: 1.35;
}
.ui-notify-close{
  margin-left: auto;
  background: transparent;
  border: 0;
  padding: 0;
  line-height: 1;
  opacity: .9;
  cursor: pointer;
}
        `;
        document.head.appendChild(style);
    }

    function ensureNotifyContainer() {
        ensureNotifyCss();
        let el = byId('uiNotifyContainer');
        if (!el) {
            el = document.createElement('div');
            el.id = 'uiNotifyContainer';
            el.className = 'ui-notify-container';
            document.body.appendChild(el);
        }
        return el;
    }

    function notify(type, msg, title, timeout = 3500) {
        const conf = notifyTypeMap[type] || notifyTypeMap.default;
        const container = ensureNotifyContainer();

        const item = document.createElement('div');
        item.className = `ui-notify ${conf.cls} ${conf.text}`;

        item.innerHTML = `
          <div class="ui-notify-body">
            <div class="ui-notify-icon">
              <span class="material-icons-outlined">${conf.icon}</span>
            </div>
            <div class="ui-notify-content">
              ${title ? `<div class="ui-notify-title">${escapeHtml(title)}</div>` : ''}
              <p class="ui-notify-text">${escapeHtml(msg ?? '')}</p>
            </div>
            <button class="ui-notify-close ${conf.close}" type="button" aria-label="Close">
              <span class="material-icons-outlined">close</span>
            </button>
          </div>
        `;

        const remove = () => {
            item.classList.remove('show');
            setTimeout(() => item.remove(), 180);
        };

        item.querySelector('.ui-notify-close')?.addEventListener('click', remove);

        container.appendChild(item);
        requestAnimationFrame(() => item.classList.add('show'));

        if (timeout && timeout > 0) setTimeout(remove, timeout);
        return item;
    }

    UI.toast = {
        success(msg, title) { return notify('success', msg, title ?? 'Success'); },
        error(msg, title) { return notify('error', msg, title ?? 'Error'); },
        warning(msg, title) { return notify('warning', msg, title ?? 'Warning'); },
        info(msg, title) { return notify('info', msg, title ?? 'Info'); },
        show({ type = 'default', msg = '', title = '', timeout = 3500 } = {}) {
            return notify(type, msg, title, timeout);
        }
    };

    // ---------- 2) Confirm dialogs (Delete confirmation) ----------
    UI.confirm = {
        async basic(message, opts = {}) {
            const title = opts.title ?? 'Confirm';
            const okText = opts.okText ?? 'OK';
            const cancelText = opts.cancelText ?? 'Cancel';

            // إذا عندك مودال جاهز نستخدمه بدل window.confirm (أجمل)
            // ولو ما موجود fallback
            if (byId('appModal')) {
                return UI.modal.confirm({ title, message, okText, cancelText });
            }
            return Promise.resolve(window.confirm(message ?? 'Are you sure?'));
        },

        async delete(message = 'هل أنت متأكد من الحذف؟', opts = {}) {
            return UI.confirm.basic(message, {
                title: opts.title ?? 'Delete',
                okText: opts.okText ?? 'Delete',
                cancelText: opts.cancelText ?? 'Cancel'
            });
        }
    };

    // ---------- 3) Loading overlay / spinner ----------
    let loadingTimer = null;

    UI.loading = {
        // ✅ show(text, autoHideMs)
        show(text = 'Loading...', autoHideMs = null) {
            const el = byId('appLoading');
            if (!el) return;

            const textEl = el.querySelector('.app-loading__text');
            if (textEl) textEl.textContent = text;

            el.classList.remove('d-none');
            el.setAttribute('aria-hidden', 'false');

            // ✅ auto hide after ms
            if (loadingTimer) {
                clearTimeout(loadingTimer);
                loadingTimer = null;
            }
            if (typeof autoHideMs === 'number' && autoHideMs > 0) {
                loadingTimer = setTimeout(() => UI.loading.hide(), autoHideMs);
            }
        },
        hide() {
            const el = byId('appLoading');
            if (!el) return;

            if (loadingTimer) {
                clearTimeout(loadingTimer);
                loadingTimer = null;
            }

            el.classList.add('d-none');
            el.setAttribute('aria-hidden', 'true');
        }
    };

    // ---------- 4) Modal manager (open/close + pass data) ----------
    UI.modal = {
        open({ title, bodyHtml, footerHtml, size }) {
            const modal = ensureModal();
            if (!modal) return;
            setModalContent({ title, bodyHtml, footerHtml, size });
            modal.instance.show();
        },
        close() {
            const modal = ensureModal();
            if (!modal) return;
            modal.instance.hide();
        },

        async confirm({ title = 'Confirm', message = 'Are you sure?', okText = 'OK', cancelText = 'Cancel' }) {
            return new Promise((resolve) => {
                const modal = ensureModal();
                if (!modal) return resolve(false);

                const footer = `
                  <button type="button" class="btn btn-secondary" data-action="cancel">${escapeHtml(cancelText)}</button>
                  <button type="button" class="btn btn-primary" data-action="ok">${escapeHtml(okText)}</button>
                `;

                UI.modal.open({
                    title,
                    bodyHtml: `<p class="mb-0">${escapeHtml(message)}</p>`,
                    footerHtml: footer,
                    size: 'modal-sm'
                });

                const footerEl = byId('appModalFooter');
                const onClick = (e) => {
                    const btn = e.target.closest?.('[data-action]');
                    const action = btn?.getAttribute?.('data-action');
                    if (!action) return;

                    footerEl?.removeEventListener('click', onClick);
                    UI.modal.close();
                    resolve(action === 'ok');
                };
                footerEl?.addEventListener('click', onClick);
            });
        }
    };

    // ---------- 5) Formatting helpers ----------
    UI.format = {
        date(value, locale = 'ar', opts) {
            if (!value) return '';
            const d = (value instanceof Date) ? value : new Date(value);
            const options = opts ?? { year: 'numeric', month: '2-digit', day: '2-digit' };
            return new Intl.DateTimeFormat(locale, options).format(d);
        },
        datetime(value, locale = 'ar', opts) {
            if (!value) return '';
            const d = (value instanceof Date) ? value : new Date(value);
            const options = opts ?? { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' };
            return new Intl.DateTimeFormat(locale, options).format(d);
        },
        money(amount, currency = 'SAR', locale = 'ar-SA') {
            const n = Number(amount ?? 0);
            return new Intl.NumberFormat(locale, { style: 'currency', currency }).format(n);
        },
        number(value, locale = 'ar', opts) {
            const n = Number(value ?? 0);
            return new Intl.NumberFormat(locale, opts ?? {}).format(n);
        }
    };

    // ---------- init ----------
    UI.init = function () {
        ensureModal();
        ensureNotifyContainer();
    };

    window.UI = UI;
})();
