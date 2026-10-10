// Expands nested scroll containers, grows same-origin iframes and hides the capture
// card before a full-page screenshot. The restore ledger is created BEFORE the first
// mutation so a later failure (or a lost command response) can still restore every
// element that was already touched. Original inline styles, priorities, container
// scroll offsets, iframe heights/max-heights and window scroll are stored.
//
// A failure that is NOT the expected cross-origin DOM restriction must abort the
// prepare: the ledger stays published so the C# finally restores whatever was already
// changed, and the thrown error is seen by the caller as a page script exception, so a
// half-expanded page is rejected instead of being screenshotted as if it succeeded
// (R5-2). Only a cross-origin document the page cannot read is an expected skip and is
// left to the CDP frame path.
(() => {
  if (window.__cccCaptureState) return 'already-prepared';
  const state = {
    prev: [],
    frames: [],
    widget: null,
    widgetDisplay: '',
    widgetDisplayPriority: '',
    widgetHiddenAtPrepare: false,
    scrollX: window.scrollX,
    scrollY: window.scrollY
  };
  // Publish the ledger first: every subsequent step can only add to it.
  window.__cccCaptureState = state;
  const failures = [];
  const describe = (error) => (error && error.message) ? error.message : String(error);
  const isCrossOriginRestriction = (error) => {
    if (!error) return false;
    const name = String(error.name || '');
    const message = String(error.message || error);
    return name === 'SecurityError' || /cross-origin|Blocked a frame|denied|Permission/i.test(message);
  };
  const fail = (label, error) => { failures.push(label + ':' + describe(error)); };
  const remember = (el, properties) => {
    const entry = { el };
    for (const property of properties) {
      entry[property] = el.style.getPropertyValue(property);
      entry[property + 'Priority'] = el.style.getPropertyPriority(property);
    }
    return entry;
  };

  // Scroll containers. A computed-style read or a height/max-height/overflow-y write
  // that unexpectedly throws is a real prepare failure, never silently skipped: the
  // element would stay unexpanded and the saved image would be truncated.
  try {
    const all = [...document.querySelectorAll('*')].reverse();
    for (const el of all) {
      try {
        if (el.id === 'ccc-widget-host' || (el.closest && el.closest('#ccc-widget-host'))) continue;
        const s = getComputedStyle(el);
        const scrollable = (s.overflowY === 'auto' || s.overflowY === 'scroll' || s.overflow === 'auto' || s.overflow === 'scroll');
        if (!scrollable || el.scrollHeight <= el.clientHeight + 20) continue;
        state.prev.push(Object.assign(remember(el, ['height', 'max-height', 'overflow-y']), {
          scrollTop: el.scrollTop,
          scrollLeft: el.scrollLeft
        }));
        el.style.setProperty('height', el.scrollHeight + 'px', 'important');
        el.style.setProperty('max-height', 'none', 'important');
        el.style.setProperty('overflow-y', 'visible', 'important');
      } catch (error) {
        // The ledger entry, when already pushed, holds this element's original values,
        // so the C# finally can still restore it; the failure is recorded and rejects.
        fail('scroll', error);
      }
    }
  } catch (error) { fail('scan', error); }

  // Same-origin frames: let the frame element grow to its document height so the
  // expanded inner content is actually painted in the parent page screenshot.
  let frames;
  try { frames = document.querySelectorAll('iframe'); }
  catch (error) { fail('frame-list', error); frames = []; }
  for (const frame of frames) {
    let inner;
    try {
      inner = frame.contentDocument;
      if (!inner || !inner.documentElement) continue;
    } catch (error) {
      // Only a cross-origin document the page cannot read is expected here.
      if (isCrossOriginRestriction(error)) continue;
      fail('frame-access', error);
      continue;
    }
    try {
      const target = Math.max(inner.documentElement.scrollHeight, inner.body ? inner.body.scrollHeight : 0);
      if (target <= frame.clientHeight) continue;
      state.frames.push(Object.assign(remember(frame, ['height', 'max-height']), { target: target }));
      frame.style.setProperty('height', target + 'px', 'important');
      frame.style.setProperty('max-height', 'none', 'important');
    } catch (error) {
      // Reading inner metrics of a cross-origin frame hits the same restriction and is
      // still the expected skip handled by the CDP frame path; anything else rejects.
      if (isCrossOriginRestriction(error)) continue;
      fail('frame-grow', error);
    }
  }

  // Only a fully prepared page may report the prepared marker. An unexpected failure
  // above is surfaced to C# so its error branch runs and the finally restores the page.
  if (failures.length > 0) throw new Error('prepare-failed:' + failures.join('|'));

  const widget = document.getElementById('ccc-widget-host');
  state.widget = widget;
  state.widgetDisplay = widget ? widget.style.getPropertyValue('display') : '';
  state.widgetDisplayPriority = widget ? widget.style.getPropertyPriority('display') : '';
  state.widgetHiddenAtPrepare = !!widget;
  if (widget) widget.style.setProperty('display', 'none', 'important');
  window.scrollTo(0, 0);
  // Test-visible marker that the real prepare path ran, used by the controlled
  // result-change scenario to mutate the result mid-capture.
  window.__cccPrepareAt = Date.now();
  return 'prepared:' + state.prev.length + ':' + state.frames.length;
})()
