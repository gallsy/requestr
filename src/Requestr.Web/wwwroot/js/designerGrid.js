window.designerGrid = (() => {
    const instances = new WeakMap();

    function initialize(root, reference) {
        dispose(root);
        const listeners = new AbortController();
        let gesture = null;
        let suppressClickUntil = 0;
        let committing = false;
        const preview = document.createElement('div');
        preview.className = 'grid-drop-preview';
        const guides = [];

        function cancel() {
            if (gesture && root.hasPointerCapture(gesture.pointerId)) root.releasePointerCapture(gesture.pointerId);
            gesture = null;
            preview.remove();
            guides.splice(0).forEach(guide => guide.remove());
            root.classList.remove('designer-interacting');
        }

        function measure(grid) {
            const style = getComputedStyle(grid);
            const columns = Number(grid.dataset.columns);
            const gap = parseFloat(style.columnGap) || 0;
            const rowGap = parseFloat(style.rowGap) || 0;
            const rect = grid.getBoundingClientRect();
            const track = (rect.width - (columns - 1) * gap) / columns;
            const rows = style.gridTemplateRows.split(' ').map(Number.parseFloat);
            return { columns, gap, rowGap, rect, track, rows };
        }

        function update(event) {
            if (!gesture || event.pointerId !== gesture.pointerId) return;
            if (!gesture.active && Math.hypot(event.clientX - gesture.x, event.clientY - gesture.y) < 5) return;
            if (!gesture.active) {
                root.querySelectorAll('.canvas-fields').forEach(grid => {
                    const geometry = measure(grid);
                    let top = 0;
                    geometry.rows.forEach(height => {
                        const guide = document.createElement('div');
                        guide.className = 'grid-row-guide';
                        Object.assign(guide.style, { top: `${top}px`, height: `${height}px`, backgroundSize: `${geometry.track + geometry.gap}px 100%` });
                        grid.append(guide);
                        guides.push(guide);
                        top += height + geometry.rowGap;
                    });
                });
            }
            gesture.active = true;
            event.preventDefault();
            root.classList.add('designer-interacting');
            const grids = [...root.querySelectorAll('.canvas-fields')];
            const grid = gesture.action === 'resize' ? gesture.sourceGrid : grids.find(candidate => {
                const rect = candidate.getBoundingClientRect();
                return event.clientX >= rect.left && event.clientX <= rect.right && event.clientY >= rect.top && event.clientY <= rect.bottom;
            });
            gesture.target = null;
            if (!grid || !grid.isConnected) { preview.remove(); return; }
            const geometry = measure(grid);
            if (!(geometry.track > 0)) return;
            grid.style.setProperty('--grid-track-width', `${geometry.track + geometry.gap}px`);
            let column = Math.floor((event.clientX - geometry.rect.left) / (geometry.track + geometry.gap)) + 1;
            let row = 1;
            let top = 0;
            let span = gesture.span;
            if (gesture.action === 'resize') {
                column = gesture.column;
                row = gesture.row;
                span += Math.round((event.clientX - gesture.x) / (geometry.track + geometry.gap));
                top = geometry.rows.slice(0, row - 1).reduce((sum, height) => sum + height + geometry.rowGap, 0);
            } else {
                while (row < geometry.rows.length && event.clientY - geometry.rect.top >= top + geometry.rows[row - 1] + geometry.rowGap) {
                    top += geometry.rows[row - 1] + geometry.rowGap;
                    row++;
                }
            }
            const permitted = root.dataset.presentationOnly !== 'true' ||
                (gesture.action !== 'add' && (gesture.sourceGrid?.dataset.condition ?? '') === grid.dataset.condition);
            const collision = [...grid.querySelectorAll('.canvas-field')].some(field => field.dataset.fieldName !== gesture.name &&
                Number(field.dataset.row) === row && Number(field.dataset.column) < column + span &&
                Number(field.dataset.column) + Number(field.dataset.span) > column);
            const valid = permitted && !collision && row <= 1000 && column >= 1 && span >= 1 && column + span - 1 <= geometry.columns;
            grid.append(preview);
            preview.classList.toggle('invalid', !valid);
            Object.assign(preview.style, {
                left: `${(column - 1) * (geometry.track + geometry.gap)}px`, top: `${top}px`,
                width: `${Math.max(1, span) * geometry.track + (Math.max(1, span) - 1) * geometry.gap}px`,
                height: `${geometry.rows[row - 1] || 88}px`
            });
            if (valid) gesture.target = { section: Number(grid.dataset.sectionId), row, column, span };
        }

        root.addEventListener('pointerdown', event => {
            const handle = event.target.closest('[data-grid-action]');
            if (!handle || committing || event.button !== 0 || !matchMedia('(min-width: 768px)').matches) return;
            const field = handle.closest('.canvas-field');
            gesture = { action: handle.dataset.gridAction, name: (field || handle).dataset.fieldName,
                sourceGrid: field?.closest('.canvas-fields'), span: Number(field?.dataset.span || 1),
                row: Number(field?.dataset.row || 1), column: Number(field?.dataset.column || 1),
                x: event.clientX, y: event.clientY, pointerId: event.pointerId, active: false, target: null };
            root.setPointerCapture(event.pointerId);
            event.preventDefault();
        }, { signal: listeners.signal });
        root.addEventListener('pointermove', update, { signal: listeners.signal });
        root.addEventListener('pointerup', async event => {
            if (!gesture || gesture.pointerId !== event.pointerId) return;
            if (gesture.active) update(event);
            const finished = gesture;
            if (finished.active) suppressClickUntil = performance.now() + 300;
            cancel();
            if (!finished.target) return;
            committing = true;
            try {
                await reference.invokeMethodAsync('CommitPlacement', finished.name, finished.target.section,
                    finished.target.row, finished.target.column, finished.target.span, finished.action === 'add');
            } catch (error) { console.error('Grid placement failed.', error); }
            finally { committing = false; }
        }, { signal: listeners.signal });
        root.addEventListener('click', event => {
            if (performance.now() < suppressClickUntil) { event.preventDefault(); event.stopImmediatePropagation(); }
        }, { capture: true, signal: listeners.signal });
        root.addEventListener('pointercancel', cancel, { signal: listeners.signal });
        root.addEventListener('lostpointercapture', cancel, { signal: listeners.signal });
        document.addEventListener('keydown', event => { if (event.key === 'Escape') cancel(); }, { signal: listeners.signal });
        window.addEventListener('blur', cancel, { signal: listeners.signal });
        window.addEventListener('resize', cancel, { signal: listeners.signal });
        const removalObserver = new MutationObserver(() => { if (!root.isConnected) dispose(root); });
        removalObserver.observe(document.body, { childList: true, subtree: true });
        instances.set(root, () => { cancel(); listeners.abort(); removalObserver.disconnect(); });
    }

    function dispose(root) {
        instances.get(root)?.();
        instances.delete(root);
    }

    return { initialize, dispose };
})();