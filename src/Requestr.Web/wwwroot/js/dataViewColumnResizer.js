// Simple column resize helper for DataView
// Attaches mousemove/mouseup on document to track width changes
(function(){
  const state = {
    active: null,
    startX: 0,
    startWidth: 0,
    dotNet: null
  };

  function onMouseMove(e){
    if(!state.active) return;
    const delta = e.clientX - state.startX;
    const newWidth = Math.max(40, state.startWidth + delta);
    state.active.style.width = newWidth + 'px';
    state.active.style.minWidth = newWidth + 'px';
    state.active.style.maxWidth = newWidth + 'px';
    const columnName = state.active.getAttribute('data-column');
    if(state.dotNet){
      state.dotNet.invokeMethodAsync('OnColumnResize', columnName, newWidth);
    }
  }

  function onMouseUp(e){
    if(!state.active) return;
    const columnName = state.active.getAttribute('data-column');
    const widthPx = parseInt(state.active.style.width,10) || state.startWidth;
    if(state.dotNet){
      state.dotNet.invokeMethodAsync('OnColumnResizeEnd', columnName, widthPx);
    }
    state.active.classList.remove('resizing-column');
    state.active = null;
    document.removeEventListener('mousemove', onMouseMove);
    document.removeEventListener('mouseup', onMouseUp);
  }

  window.dataViewColumnResizer = {
    begin: function(dotNetRef, columnName, startWidth, startClientX){
      const th = document.querySelector(`th.resizable-header[data-column="${columnName}"]`);
      if(!th) return;
      state.active = th;
      state.startX = typeof startClientX === 'number' ? startClientX : (window.event ? window.event.clientX : 0);
      state.startWidth = startWidth || th.offsetWidth;
      state.dotNet = dotNetRef;
      th.classList.add('resizing-column');
      document.addEventListener('mousemove', onMouseMove);
      document.addEventListener('mouseup', onMouseUp);
    },
    detach: function(){
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
      state.active = null;
    }
  };
})();
