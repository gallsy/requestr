// Modal utility functions
window.showModal = (element) => {
    if (element && window.bootstrap) {
        const modal = bootstrap.Modal.getOrCreateInstance(element);
        modal.show();
    }
};

window.hideModal = (element) => {
    if (element && window.bootstrap) {
        const modal = bootstrap.Modal.getOrCreateInstance(element);
        modal.hide();
    }
};

// Toast utility functions
window.showToast = (message, type = 'info') => {
    // Create toast container if it doesn't exist
    let toastContainer = document.querySelector('.toast-container');
    if (!toastContainer) {
        toastContainer = document.createElement('div');
        toastContainer.className = 'toast-container position-fixed bottom-0 end-0 p-3';
        document.body.appendChild(toastContainer);
    }

    // Create toast element
    const toastId = 'toast-' + Date.now();
    const toastHtml = `
        <div id="${toastId}" class="toast" role="alert" aria-live="assertive" aria-atomic="true">
            <div class="toast-header">
                <strong class="me-auto">${window.__APP_BRAND__ || 'Requestr'}</strong>
                <button type="button" class="btn-close" data-bs-dismiss="toast" aria-label="Close"></button>
            </div>
            <div class="toast-body bg-${type} text-white">
                ${message}
            </div>
        </div>
    `;

    toastContainer.insertAdjacentHTML('beforeend', toastHtml);
    
    // Show the toast
    const toastElement = document.getElementById(toastId);
    if (toastElement && window.bootstrap) {
        const toast = new bootstrap.Toast(toastElement);
        toast.show();
        
        // Remove toast element after it's hidden
        toastElement.addEventListener('hidden.bs.toast', () => {
            toastElement.remove();
        });
    }
};

// Buttons in the reconnect dialog (_Layout.cshtml)
document.addEventListener('click', event => {
    const button = event.target.closest('[data-reconnect-action]');
    if (!button) return;
    if (button.dataset.reconnectAction === 'reload') {
        location.reload();
        return;
    }
    const modal = document.getElementById('components-reconnect-modal');
    const paused = modal?.classList.contains('components-reconnect-paused') || modal?.classList.contains('components-reconnect-resume-failed');
    const attempt = paused ? window.Blazor?.resumeCircuit?.() : window.Blazor?.reconnect?.();
    // false means the server no longer knows this circuit, so only a reload can recover.
    Promise.resolve(attempt).then(ok => { if (ok === false) location.reload(); }).catch(() => location.reload());
});

// Focus management
window.focusElement = (elementId) => {
    const element = document.getElementById(elementId);
    if (element) {
        element.focus();
    }
};

// Scroll to element
window.scrollToElement = (elementId) => {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollIntoView({ behavior: 'smooth' });
    }
};

// Combobox keyboard handling
const comboboxKeydownHandlers = new WeakMap();

window.requestrCombobox = {
    initialize: (input) => {
        if (!input || comboboxKeydownHandlers.has(input)) {
            return;
        }

        const handler = (event) => {
            if (event.key === 'Enter') {
                // Blazor still receives the keydown event and selects the option,
                // but the browser does not submit the containing form.
                event.preventDefault();
            }
        };

        input.addEventListener('keydown', handler);
        comboboxKeydownHandlers.set(input, handler);
    },

    dispose: (input) => {
        const handler = input ? comboboxKeydownHandlers.get(input) : null;
        if (!handler) {
            return;
        }

        input.removeEventListener('keydown', handler);
        comboboxKeydownHandlers.delete(input);
    }
};

// Backward-compatible name used by the strict searchable dropdown component.
window.requestrSearchableSelect = window.requestrCombobox;

// Form builder: publish the sticky header height to CSS and handle Ctrl/Cmd+S.
window.requestrBuilder = (() => {
    let observer = null;
    let keyHandler = null;

    function dispose() {
        observer?.disconnect();
        observer = null;
        if (keyHandler) document.removeEventListener('keydown', keyHandler);
        keyHandler = null;
        document.documentElement.style.removeProperty('--builder-header-height');
    }

    function initialize(header, dotNetRef) {
        dispose();
        if (header && window.ResizeObserver) {
            const apply = () => document.documentElement.style.setProperty('--builder-header-height', `${header.offsetHeight}px`);
            observer = new ResizeObserver(apply);
            observer.observe(header);
            apply();
        }
        keyHandler = event => {
            if (!(event.ctrlKey || event.metaKey) || event.altKey || event.key.toLowerCase() !== 's') return;
            event.preventDefault();
            // Blur first so a pending input change reaches Blazor before the save runs.
            const active = document.activeElement;
            active?.blur?.();
            dotNetRef.invokeMethodAsync('SaveFromShortcut')
                .catch(() => { })
                .finally(() => active?.focus?.());
        };
        document.addEventListener('keydown', keyHandler);
    }

    return { initialize, dispose };
})();
