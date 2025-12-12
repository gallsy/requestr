// Form Builder JavaScript helpers for drag-and-drop functionality

window.formBuilderInterop = {
    // Track if sticky palette is initialized
    _stickyInitialized: false,
    _scrollHandler: null,

    // Initialize sticky field palette behavior
    initStickyPalette: function () {
        if (this._stickyInitialized) return;
        
        const palette = document.querySelector('.field-palette');
        const column = document.querySelector('.field-palette-column');
        
        if (!palette || !column) {
            // Retry after a short delay if elements aren't ready
            setTimeout(() => this.initStickyPalette(), 100);
            return;
        }

        const headerHeight = 80; // Account for top header
        const bottomMargin = 20;
        
        // Store original position info
        const columnRect = column.getBoundingClientRect();
        const originalTop = columnRect.top + window.scrollY;
        const columnWidth = column.offsetWidth;

        this._scrollHandler = function () {
            const scrollY = window.scrollY;
            const viewportHeight = window.innerHeight;
            const paletteHeight = palette.offsetHeight;
            
            // Calculate maximum scroll before palette would go off-screen
            const maxScroll = originalTop - headerHeight;
            
            if (scrollY > maxScroll) {
                // Make palette fixed
                palette.style.position = 'fixed';
                palette.style.top = headerHeight + 'px';
                palette.style.width = columnWidth + 'px';
                palette.style.maxHeight = (viewportHeight - headerHeight - bottomMargin) + 'px';
                palette.classList.add('is-sticky');
            } else {
                // Reset to normal positioning
                palette.style.position = '';
                palette.style.top = '';
                palette.style.width = '';
                palette.style.maxHeight = '';
                palette.classList.remove('is-sticky');
            }
        };

        window.addEventListener('scroll', this._scrollHandler, { passive: true });
        window.addEventListener('resize', this._scrollHandler, { passive: true });
        
        // Initial check
        this._scrollHandler();
        
        this._stickyInitialized = true;
        console.log('Sticky field palette initialized');
    },

    // Cleanup sticky palette (call when leaving the page)
    destroyStickyPalette: function () {
        if (this._scrollHandler) {
            window.removeEventListener('scroll', this._scrollHandler);
            window.removeEventListener('resize', this._scrollHandler);
            this._scrollHandler = null;
        }
        this._stickyInitialized = false;
        
        const palette = document.querySelector('.field-palette');
        if (palette) {
            palette.style.position = '';
            palette.style.top = '';
            palette.style.width = '';
            palette.style.maxHeight = '';
            palette.classList.remove('is-sticky');
        }
    },

    // Initialize drag-and-drop event listeners for better browser support
    initDragDrop: function () {
        // Add global dragover handler to allow drops
        document.addEventListener('dragover', function (e) {
            // Check if the target is a drop zone
            if (e.target.closest('.section-grid') || e.target.closest('.grid-cell.empty')) {
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
            }
        });

        // Add dragenter handler for visual feedback
        document.addEventListener('dragenter', function (e) {
            const dropZone = e.target.closest('.grid-cell.empty');
            if (dropZone) {
                dropZone.classList.add('drag-hover');
            }
        });

        // Add dragleave handler to remove visual feedback
        document.addEventListener('dragleave', function (e) {
            const dropZone = e.target.closest('.grid-cell.empty');
            if (dropZone && !dropZone.contains(e.relatedTarget)) {
                dropZone.classList.remove('drag-hover');
            }
        });

        // Add drop handler to clean up
        document.addEventListener('drop', function (e) {
            document.querySelectorAll('.grid-cell.empty.drag-hover').forEach(function (el) {
                el.classList.remove('drag-hover');
            });
        });

        console.log('Form Builder drag-and-drop initialized');
    },

    // Set data for drag operation (needed for cross-browser compatibility)
    setDragData: function (e, data) {
        if (e && e.dataTransfer) {
            e.dataTransfer.setData('text/plain', data);
            e.dataTransfer.effectAllowed = 'move';
        }
    },

    // Clean up drag visual states
    cleanupDragState: function () {
        document.querySelectorAll('.drag-hover').forEach(function (el) {
            el.classList.remove('drag-hover');
        });
        document.querySelectorAll('.drag-over').forEach(function (el) {
            el.classList.remove('drag-over');
        });
    }
};

// Auto-initialize when the script loads
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function () {
        window.formBuilderInterop.initDragDrop();
    });
} else {
    window.formBuilderInterop.initDragDrop();
}