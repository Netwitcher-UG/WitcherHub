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
        success: { cls: 'bg-success-main', icon: 'ri-checkbox-circle-line', text: 'text-white', close: 'text-white' },
        error: { cls: 'bg-danger-main', icon: 'ri-close-circle-line', text: 'text-white', close: 'text-white' },
        warning: { cls: 'bg-warning-main', icon: 'ri-error-warning-line', text: 'text-dark', close: 'text-dark' },
        info: { cls: 'bg-info-main', icon: 'ri-information-line', text: 'text-white', close: 'text-white' },
        default: { cls: 'bg-primary-600', icon: 'ri-notification-3-line', text: 'text-white', close: 'text-white' }
    };

    // Toast styling lives in wwwroot/css/ui-kit.css.
    //
    // A second copy of the same rules used to be injected here at runtime.
    // Being injected after the linked stylesheet, it won the cascade — so
    // editing ui-kit.css changed nothing, and the toast stayed anchored at the
    // top right where it covered the action buttons in every page header.

    function ensureNotifyContainer() {
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
              <i class="${conf.icon}"></i>
            </div>
            <div class="ui-notify-content">
              ${title ? `<div class="ui-notify-title">${escapeHtml(title)}</div>` : ''}
              <p class="ui-notify-text">${escapeHtml(msg ?? '')}</p>
            </div>
            <button class="ui-notify-close ${conf.close}" type="button" aria-label="Close">
              <i class="ri-close-line"></i>
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

        // timeout: 0 keeps the toast until it is closed by hand. Long-running
        // work needs that: its result arrives minutes after the click, when the
        // user is usually in another tab, and a message that removes itself has
        // already gone by the time they come back.
        show({ type = 'default', msg = '', title = '', timeout = 3500 } = {}) {
            return notify(type, msg, title, timeout);
        }
    };

    // ---------- Notification sound ----------
    //
    // For work that finishes minutes after the click — asked for by the owner
    // for the AI actions specifically, because they switch tabs while the model
    // reads a contract and a silent toast in a hidden tab tells nobody anything.
    //
    // Two rules the Web Audio API imposes shape this:
    //   * an AudioContext only starts inside a user gesture, so prime() must be
    //     called from the click that starts the work;
    //   * a context primed while the tab was visible keeps playing when the tab
    //     is hidden, which is the whole point.
    //
    // The chime is synthesised — two soft sine notes, a major third apart —
    // so there is no audio file to fetch, cache or fail on.
    let audioCtx = null;

    UI.sound = {
        /// Call from the click that starts long work. Without a gesture the
        /// browser refuses to start audio, and a chime requested later from a
        /// background tab would stay silent.
        prime() {
            try {
                audioCtx = audioCtx || new (window.AudioContext || window.webkitAudioContext)();
                if (audioCtx.state === 'suspended') audioCtx.resume();
            } catch { /* no audio on this browser; the toast still shows */ }
        },

        chime() {
            try {
                if (!audioCtx || audioCtx.state !== 'running') return;

                const now = audioCtx.currentTime;

                [[523.25, 0], [659.25, 0.14]].forEach(([freq, delay]) => {
                    const osc = audioCtx.createOscillator();
                    const gain = audioCtx.createGain();

                    osc.type = 'sine';
                    osc.frequency.value = freq;

                    // A fast rise and a slow fall reads as a chime; equal ramps
                    // read as a beep from a machine that wants something.
                    gain.gain.setValueAtTime(0, now + delay);
                    gain.gain.linearRampToValueAtTime(0.12, now + delay + 0.02);
                    gain.gain.exponentialRampToValueAtTime(0.0001, now + delay + 0.6);

                    osc.connect(gain).connect(audioCtx.destination);
                    osc.start(now + delay);
                    osc.stop(now + delay + 0.7);
                });
            } catch { /* never let a sound break the message it decorates */ }
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

            document.body.classList.add('ui-loading');

            el.classList.remove('d-none');
            el.setAttribute('aria-hidden', 'false');

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

            document.body.classList.remove('ui-loading');
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

    // ---------- Status badges ----------
    //
    // The mirror of Pages/Models/UI/StatusPresentation.cs, for rows that are
    // rendered in the browser rather than on the server.
    //
    // Those rows used to build their own badge markup, in Bootstrap's idiom
    // (`bg-success bg-opacity-10 text-success`) rather than the theme's
    // (`bg-success-focus text-success-main border …`). The two are different
    // shades of the same colour, so a quote in the project workspace and the same
    // quote in the quotes list did not look alike. The wording had drifted too:
    // the server calls a sent quote "Awaiting customer" and a sent contract
    // "Awaiting signature", while these scripts called both "Sent" — the status
    // that says least about who is now waiting for whom.
    //
    // Keep the maps below in step with the C# ones. There is a test that fails if
    // a script starts emitting badge markup of its own instead of calling this.

    const BADGE_TONES = {
        success: 'bg-success-focus text-success-main border border-success-main',
        danger: 'bg-danger-focus text-danger-main border border-danger-main',
        warning: 'bg-warning-focus text-warning-main border border-warning-main',
        info: 'bg-info-focus text-info-main border border-info-main',
        primary: 'bg-primary-50 text-primary-600 border border-primary-600',
        neutral: 'bg-neutral-200 text-neutral-600 border border-neutral-400'
    };

    // Statuses arrive from the API as strings and their casing is not guaranteed.
    function key(status) {
        return String(status ?? '').trim().toLowerCase();
    }

    const STATUS_MAPS = {
        quote: {
            draft: ['Draft', 'secondary'],
            sent: ['Awaiting customer', 'warning'],
            accepted: ['Accepted', 'success'],
            signed: ['Signed', 'success'],
            rejected: ['Rejected', 'danger'],
            cancelled: ['Cancelled', 'secondary'],
            void: ['Void', 'secondary']
        },
        contract: {
            draft: ['Draft', 'secondary'],
            sent: ['Awaiting signature', 'warning'],
            signed: ['Signed', 'success'],
            accepted: ['Accepted', 'success'],
            rejected: ['Rejected', 'danger'],
            terminated: ['Terminated', 'danger'],
            cancelled: ['Cancelled', 'secondary'],
            void: ['Void', 'secondary']
        },
        invoice: {
            draft: ['Draft', 'secondary'],
            issued: ['Issued', 'info'],
            sent: ['Sent', 'info'],
            open: ['Open', 'warning'],
            overdue: ['Overdue', 'danger'],
            paid: ['Paid', 'success'],
            cancelled: ['Cancelled', 'secondary'],
            void: ['Void', 'secondary']
        },
        project: {
            draft: ['Draft', 'secondary'],
            active: ['Active', 'success'],
            onhold: ['On hold', 'warning'],
            closed: ['Closed', 'info'],
            cancelled: ['Cancelled', 'danger']
        }
    };

    UI.badge = {
        /**
         * A badge in the theme's markup. Unknown tones come out neutral rather
         * than unstyled.
         */
        html(label, tone = 'neutral') {
            const classes = BADGE_TONES[tone] ?? BADGE_TONES.neutral;
            return `<span class="badge ${classes} px-16 py-4 radius-4">${escapeHtml(label)}</span>`;
        },

        /**
         * A document status, worded and coloured the way the server words and
         * colours it. `kind` is one of quote, contract, invoice, project.
         *
         * An unrecognised status is shown as it arrived rather than hidden or
         * guessed at — if the backend grows a status the UI has not been taught,
         * the user should see its name, not a blank space.
         */
        status(kind, status) {
            const map = STATUS_MAPS[kind];
            const entry = map?.[key(status)];

            if (!entry) return this.html(status || '—', 'secondary');

            return this.html(entry[0], entry[1]);
        },

        /** A yes/no state: green when true, quiet when not. */
        toggle(on, whenOn, whenOff) {
            return this.html(on ? whenOn : whenOff, on ? 'success' : 'neutral');
        }
    };

    // ---------- init ----------
    UI.init = function () {
        ensureModal();
        ensureNotifyContainer();
    };

    window.UI = UI;
})();
