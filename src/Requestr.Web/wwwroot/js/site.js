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
