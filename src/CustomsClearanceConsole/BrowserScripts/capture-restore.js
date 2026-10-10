// Restores every temporary change made by capture-prepare.js, including container
// scroll offsets and grown iframe heights/max-heights with their original priority,
// and records whether styles, scroll offsets and the window scroll really returned to
// their originals. Runs in a finally block with its own short timeout so a failed or
// rejected capture never leaves the page modified.
(() => {
  const state = window.__cccCaptureState;
  if (!state) return 'nothing-to-restore';
  const sameStyle = (el, property, value) => (el.style.getPropertyValue(property) || '') === (value || '');
  const restoreProperty = (el, item, property) => {
    const value = item[property];
    const priority = item[property + 'Priority'] || '';
    if (value) el.style.setProperty(property, value, priority);
    else el.style.removeProperty(property);
  };
  let stylesRestored = true;
  let scrollRestored = true;
  for (const item of state.prev || []) {
    const el = item.el;
    if (!el || !el.style) continue;
    restoreProperty(el, item, 'height');
    restoreProperty(el, item, 'max-height');
    restoreProperty(el, item, 'overflow-y');
    // The ledger stores each value under its CSS property name (see remember() in prepare).
    if (!sameStyle(el, 'height', item.height) || !sameStyle(el, 'max-height', item['max-height']) || !sameStyle(el, 'overflow-y', item['overflow-y']))
      stylesRestored = false;
    try {
      el.scrollTop = item.scrollTop || 0; el.scrollLeft = item.scrollLeft || 0;
      if (Math.abs((el.scrollTop || 0) - (item.scrollTop || 0)) > 1 || Math.abs((el.scrollLeft || 0) - (item.scrollLeft || 0)) > 1)
        scrollRestored = false;
    } catch (error) { scrollRestored = false; }
  }
  for (const item of state.frames || []) {
    const el = item.el;
    if (!el || !el.style) continue;
    restoreProperty(el, item, 'height');
    restoreProperty(el, item, 'max-height');
    if (!sameStyle(el, 'height', item.height) || !sameStyle(el, 'max-height', item['max-height'])) stylesRestored = false;
  }
  const widget = state.widget || document.getElementById('ccc-widget-host');
  if (widget && widget.style) restoreProperty(widget, {
    display: state.widgetDisplay,
    displayPriority: state.widgetDisplayPriority
  }, 'display');
  try { window.scrollTo(state.scrollX || 0, state.scrollY || 0); } catch (error) { /* ignore */ }
  const windowRestored = Math.abs(window.scrollX - (state.scrollX || 0)) <= 1 && Math.abs(window.scrollY - (state.scrollY || 0)) <= 1;
  window.__cccLastCapture = {
    restored: state.prev ? state.prev.length : 0,
    framesRestored: state.frames ? state.frames.length : 0,
    widgetWasHidden: !!state.widget,
    widgetHiddenAtPrepare: !!state.widgetHiddenAtPrepare,
    stylesRestored,
    scrollRestored: scrollRestored && windowRestored,
    widgetVisibleAfterRestore: !widget || widget.style.getPropertyValue('display') !== 'none'
  };
  delete window.__cccCaptureState;
  return 'restored:' + (state.prev ? state.prev.length : 0);
})()
