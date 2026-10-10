// Injected floating capture card (design spec section 5, web/capture-widget.html).
// Closed Shadow DOM + ccc- prefix so the host page cannot read or restyle it and
// its own text never contributes to the light-DOM result verification.
(() => {
  const declarationNo = __DECLARATION_NO__;
  const saveDir = __SAVE_DIR__;
  const STATE_KEY = '__cccWidgetInstalled';
  // The card belongs to the top-level page only. The per-document script also runs in
  // same-process child frames (the official query frame on swapp.singlewindow.cn is
  // same-site with www.singlewindow.cn), which used to mount a second card inside the
  // query frame: its click started a capture whose progress and result only ever reached
  // the top card, so the frame card stayed at "截取中" forever.
  let isTop = true;
  try { isTop = window.top === window; } catch (error) { isTop = false; }
  if (!isTop) return 'subframe';

  function install() {
    const root = document.documentElement;
    if (!root) return 'no-root';
    const existing = document.getElementById('ccc-widget-host');
    if (window[STATE_KEY] === declarationNo && existing) return 'already-installed';
    if (existing) existing.remove();

    const host = document.createElement('div');
    host.id = 'ccc-widget-host';
    host.style.cssText = 'all:initial;position:fixed;right:24px;bottom:24px;z-index:2147483647;';
    const shadow = host.attachShadow({ mode: 'closed' });
    const style = document.createElement('style');
    style.textContent = `
      .ccc-widget{--ccc-primary:#13355E;--ccc-accent:#1F5FAE;--ccc-ink:#0F1B2D;--ccc-ink2:#3D4A5C;
        --ccc-muted:#5B6778;--ccc-border:#C9D1DC;--ccc-ok:#17714F;--ccc-danger:#B42318;--ccc-warn:#8A5300;
        box-sizing:border-box;width:300px;padding:14px;display:flex;flex-direction:column;gap:10px;background:#fff;
        border:1px solid var(--ccc-border);border-radius:10px;box-shadow:0 12px 32px rgba(15,27,45,.22);
        font-family:'Noto Sans SC','Source Han Sans SC',system-ui,sans-serif;color:var(--ccc-ink);font-size:12px;line-height:1.55}
      .ccc-head{display:flex;align-items:center;gap:8px;cursor:move;user-select:none}
      .ccc-mark{width:20px;height:20px;border-radius:5px;background:var(--ccc-primary);display:flex;align-items:center;justify-content:center}
      .ccc-title{flex:1;font-size:12.5px;font-weight:700}
      .ccc-icon-btn{width:26px;height:26px;border:0;border-radius:4px;background:transparent;display:flex;align-items:center;justify-content:center;cursor:pointer}
      .ccc-icon-btn:hover{background:#EEF1F5}
      .ccc-number{font-family:'JetBrains Mono',monospace;font-size:14px;font-weight:500;color:var(--ccc-primary);word-break:break-all}
      .ccc-hint{color:var(--ccc-muted)}
      .ccc-path{font-family:'JetBrains Mono',monospace;font-size:11.5px;color:#6B7686;word-break:break-all}
      .ccc-btn{height:40px;border:0;border-radius:6px;background:var(--ccc-primary);color:#fff;font:inherit;font-size:14px;
        font-weight:500;display:flex;align-items:center;justify-content:center;gap:8px;cursor:pointer}
      .ccc-btn:hover{background:#0E2747}
      .ccc-btn[disabled]{background:#C3CCD8;cursor:default}
      .ccc-progress{height:6px;border-radius:3px;background:#DCE6F3;overflow:hidden}
      .ccc-progress>i{display:block;height:100%;background:var(--ccc-accent);width:0%}
      .ccc-result{display:flex;align-items:center;gap:6px;font-size:13px;font-weight:500}
      .ccc-link{color:var(--ccc-accent);font-size:12.5px;text-decoration:none;cursor:pointer}
      .ccc-s{display:none;flex-direction:column;gap:10px}
      .ccc-widget[data-state="idle"] .ccc-s-idle,
      .ccc-widget[data-state="capturing"] .ccc-s-capturing,
      .ccc-widget[data-state="saved"] .ccc-s-saved,
      .ccc-widget[data-state="mismatch"] .ccc-s-mismatch,
      .ccc-widget[data-state="too-long"] .ccc-s-too-long,
      .ccc-widget[data-state="error"] .ccc-s-error{display:flex}
      .ccc-widget[data-state="saved"]{border-color:#9FD0B8}
      .ccc-widget[data-state="mismatch"]{border-color:#F0B8B1}
      .ccc-widget[data-state="too-long"]{border-color:#E8C98A}
    `;
    shadow.appendChild(style);

    const widget = document.createElement('div');
    widget.className = 'ccc-widget';
    widget.dataset.state = 'idle';
    widget.setAttribute('role', 'dialog');
    widget.setAttribute('aria-label', '关单核验台截图');
    widget.innerHTML = `
      <div class="ccc-head">
        <span class="ccc-mark"><svg width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="#fff" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 2.5h5.5L12 5v8.5H4z"/><path d="m6 9.2 1.6 1.6L10.5 7.8"/></svg></span>
        <span class="ccc-title">关单核验台</span>
        <button class="ccc-icon-btn" type="button" aria-label="收起" data-role="collapse"><svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="#5B6778" stroke-width="1.6" stroke-linecap="round"><path d="M4 8h8"/></svg></button>
      </div>
      <div class="ccc-number"></div>
      <div class="ccc-s ccc-s-idle">
        <div class="ccc-hint">输入验证码并点击“查询”，结果出现后点击下方按钮保存整页长截图。</div>
      </div>
      <div class="ccc-s ccc-s-capturing">
        <div class="ccc-result" style="color:var(--ccc-accent)" data-role="progress-text">正在截取整页</div>
        <div class="ccc-progress"><i data-role="progress-bar"></i></div>
      </div>
      <div class="ccc-s ccc-s-saved">
        <div class="ccc-result" style="color:var(--ccc-ok)">✓ 已保存</div>
        <div class="ccc-path" data-role="file" style="color:var(--ccc-ink)"></div>
        <div class="ccc-hint">列表中该单已标记“已留存”，可关闭此网页</div>
        <a class="ccc-link" data-role="open">打开截图文件夹</a>
      </div>
      <div class="ccc-s ccc-s-mismatch">
        <div class="ccc-result" style="color:var(--ccc-danger)">未保存：单号不一致</div>
        <div style="color:var(--ccc-ink2)" data-role="mismatch-text">页面结果与待核验单号不同。请核对后重新查询。</div>
      </div>
      <div class="ccc-s ccc-s-too-long">
        <div class="ccc-result" style="color:var(--ccc-warn)">页面过长</div>
        <div style="color:var(--ccc-ink2)" data-role="too-long-text">超过 6000 万像素，请使用浏览器分段保存。</div>
      </div>
      <div class="ccc-s ccc-s-error">
        <div class="ccc-result" style="color:var(--ccc-danger)" data-role="error-text">截图未保存</div>
        <div class="ccc-hint" data-role="error-detail"></div>
      </div>
      <button class="ccc-btn" type="button" data-role="capture">
        <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="#fff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="4" width="12" height="9" rx="1.5"/><circle cx="8" cy="8.5" r="2.2"/><path d="M6 4l1-1.5h2L10 4"/></svg><span data-role="capture-label">长截图</span>
      </button>
      <div class="ccc-path" data-role="save-dir"></div>
    `;
    shadow.appendChild(widget);

    widget.querySelector('.ccc-number').textContent = declarationNo;
    // A Windows folder name may contain '&' or quotes; never parse it as markup.
    widget.querySelector('[data-role=save-dir]').textContent = '→ ' + saveDir;
    const captureButton = widget.querySelector('[data-role=capture]');
    const captureLabel = widget.querySelector('[data-role=capture-label]');
    const openLink = widget.querySelector('[data-role=open]');
    const collapse = widget.querySelector('[data-role=collapse]');

    // Input-chain counters owned by this closed-shadow closure. The E2E reads them through
    // diagnostics() to localise an unregistered real click: no DOM click only proves the
    // point never produced a button click (a paint/compositor drop or a harness miss is a
    // risk still to verify, not a proven cause); a DOM click without a binding call points
    // at the page handler; a binding call without a backend accept points at the CDP binding.
    let domClickCount = 0;
    let bindingCallCount = 0;
    // Pointer moves that the browser actually routed to the capture button. The E2E moves the
    // real mouse first and presses only once this advanced, which proves the browser-side input
    // routing (not just the page's own layout) already delivers that point to this button.
    let pointerMoveCount = 0;

    let collapsed = false;
    function setCollapsed(value) {
      collapsed = value;
      widget.querySelectorAll('.ccc-s').forEach(section => { section.style.display = collapsed ? 'none' : ''; });
      collapse.setAttribute('aria-label', collapsed ? '展开' : '收起');
    }

    // The action button stays available in every non-capturing state so a failed or
    // rejected capture can always be retried without reloading the page.
    const api = {
      declarationNo,
      host,
      isCapturing: false,
      canCapture() { return !captureButton.disabled && captureButton.getClientRects().length > 0; },
      // Rect of the visible button for a real CDP mouse click in the E2E path.
      captureButtonRect() {
        const rect = captureButton.getBoundingClientRect();
        return { x: rect.left, y: rect.top, width: rect.width, height: rect.height };
      },
      // Exact hit test inside this closed shadow root. document.elementFromPoint only proves
      // the widget host container is on top; the button itself is resolved through the closed
      // shadow root held by this closure. exactButtonHit is true only when the shadow hit is
      // the capture button or one of its descendants; rect containment is never accepted as
      // exact, and an unsupported/throwing shadow hit stays false instead of falling back to it.
      hitTest(x, y) {
        const rect = captureButton.getBoundingClientRect();
        const inside = rect.width > 0 && rect.height > 0 &&
          x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom;
        let top = null;
        try { top = document.elementFromPoint(x, y); } catch (error) { top = null; }
        const topIsHost = !!top && (top.id === 'ccc-widget-host' || (top.closest && top.closest('#ccc-widget-host')));
        let exactButtonHit = false;
        let shadowHit = 'unsupported';
        try {
          if (shadow && typeof shadow.elementFromPoint === 'function') {
            const hit = shadow.elementFromPoint(x, y);
            exactButtonHit = !!hit && (hit === captureButton || captureButton.contains(hit));
            shadowHit = hit ? ((hit.getAttribute && hit.getAttribute('data-role')) || hit.tagName || 'other') : 'none';
          }
        } catch (error) { exactButtonHit = false; shadowHit = 'error'; }
        return { inside, topIsHost, top: top ? (top.tagName || '') : 'none', exactButtonHit, shadowHit };
      },
      // Snapshot of everything the E2E needs to explain a real-click branch: rect, viewport,
      // DPR, state/disabled/isCapturing, exact hit and the closure's click/binding counters.
      diagnostics() {
        const rect = captureButton.getBoundingClientRect();
        let display = '', visibility = '';
        try { const style = getComputedStyle(host); display = style.display; visibility = style.visibility; } catch (error) { /* detached */ }
        const centerX = rect.left + rect.width / 2;
        const centerY = rect.top + rect.height / 2;
        const enabled = !captureButton.disabled;
        const visible = captureButton.getClientRects().length > 0 && display !== 'none' && visibility !== 'hidden';
        return {
          state: widget.dataset.state,
          isCapturing: !!api.isCapturing,
          disabled: !!captureButton.disabled,
          enabled,
          visible,
          canCapture: !!api.canCapture(),
          rect: { x: rect.left, y: rect.top, width: rect.width, height: rect.height },
          display,
          visibility,
          viewport: { w: window.innerWidth, h: window.innerHeight, dpr: window.devicePixelRatio },
          hit: api.hitTest(centerX, centerY),
          domClickCount,
          bindingCallCount,
          pointerMoveCount,
          collapsed,
          // Text of the section the current state shows; empty when nothing explains the state.
          message: (() => {
            const section = widget.querySelector('.ccc-s-' + widget.dataset.state);
            return section && section.getClientRects().length > 0 ? (section.innerText || '').trim() : '';
          })()
        };
      },
      // Bounded render-readiness gate used before a real CDP mouse click and before the
      // production completion is published. Resolves (never rejects) once the card is
      // enabled and visible, its rect has been stable for two animation frames and the
      // exact closed-shadow button hit is confirmed; a supplied expected viewport also has
      // to be restored first (the capture temporarily enlarges it). No blind sleep.
      whenInteractive(timeoutMs, expectedViewport) {
        const budget = timeoutMs > 0 ? timeoutMs : 2000;
        const deadline = Date.now() + budget;
        const expected = expectedViewport || null;
        return new Promise(resolve => {
          let lastRect = null;
          let stableFrames = 0;
          let settled = false;
          const finish = (ready, reason) => {
            if (settled) return;
            settled = true;
            clearTimeout(guard);
            const diag = api.diagnostics();
            resolve(Object.assign({ ready, reason }, diag));
          };
          // rAF can be throttled; the guard keeps the wait bounded and never hangs a caller.
          const guard = setTimeout(() => finish(false, 'no-frames'), budget + 500);
          const viewportRestored = diag => !expected || (
            Math.abs(diag.viewport.w - expected.w) <= 1 &&
            Math.abs(diag.viewport.h - expected.h) <= 1 &&
            Math.abs(diag.viewport.dpr - expected.dpr) <= 0.001);
          const step = () => {
            if (settled) return;
            const diag = api.diagnostics();
            if (!diag.enabled || !diag.visible || !viewportRestored(diag)) {
              lastRect = null;
              stableFrames = 0;
              if (Date.now() > deadline) { finish(false, viewportRestored(diag) ? 'not-interactive' : 'viewport-not-restored'); return; }
              requestAnimationFrame(step);
              return;
            }
            const key = [diag.rect.x, diag.rect.y, diag.rect.width, diag.rect.height].join(',');
            if (key === lastRect) stableFrames += 1; else { lastRect = key; stableFrames = 1; }
            if (stableFrames >= 2 && diag.hit.exactButtonHit && diag.hit.topIsHost) { finish(true, 'ready'); return; }
            if (Date.now() > deadline) { finish(false, 'unstable'); return; }
            requestAnimationFrame(step);
          };
          requestAnimationFrame(step);
        });
      },
      requestCapture() { if (!captureButton.disabled) captureButton.click(); },
      setCollapsed,
      setState(state, payload) {
        if (state === 'capturing') armWatchdog(); else disarmWatchdog();
        // A result must never be hidden: a card the user collapsed earlier opens again so the
        // saved file or the reason for not saving is visible next to the retry button.
        if (state !== 'idle') setCollapsed(false);
        widget.dataset.state = state;
        api.isCapturing = state === 'capturing';
        captureButton.disabled = state === 'capturing';
        if (state === 'capturing') captureLabel.textContent = '截取中';
        else if (state === 'saved') captureLabel.textContent = '重新截图';
        else if (state === 'idle') captureLabel.textContent = '长截图';
        else captureLabel.textContent = '重试截图';
        if (state === 'saved' && payload) widget.querySelector('[data-role=file]').textContent = payload.file || '';
        if (state === 'mismatch' && payload && payload.message) widget.querySelector('[data-role=mismatch-text]').textContent = payload.message;
        if (state === 'too-long' && payload && payload.message) widget.querySelector('[data-role=too-long-text]').textContent = payload.message;
        if (state === 'error' && payload) {
          widget.querySelector('[data-role=error-text]').textContent = payload.message || '截图未保存';
          widget.querySelector('[data-role=error-detail]').textContent = payload.detail || '';
        }
      },
      setProgress(done, total, text) {
        if (api.isCapturing) armWatchdog();
        widget.querySelector('[data-role=progress-text]').textContent = text || ('正在截取整页 · ' + done + ' / ' + total + ' 段');
        const pct = total > 0 ? Math.round(done / total * 100) : 0;
        widget.querySelector('[data-role=progress-bar]').style.width = pct + '%';
      }
    };
    window.__cccWidget = api;

    // Last line of defence: the console bounds every capture and always reports a result, but
    // if nothing at all comes back (the console was closed, the control connection dropped)
    // the card must not stay on "截取中" forever. Every progress update re-arms the timer.
    let watchdog = 0;
    function disarmWatchdog() { if (watchdog) { clearTimeout(watchdog); watchdog = 0; } }
    function armWatchdog() {
      disarmWatchdog();
      watchdog = setTimeout(() => {
        watchdog = 0;
        if (!api.isCapturing) return;
        api.setState('error', { message: '截图无响应，未保存', detail: '关单核验台长时间没有返回结果。请确认程序仍在运行后重试；如反复出现，请提供 app.log。' });
      }, api.watchdogMs || 90000);
    }

    captureButton.addEventListener('pointermove', () => { pointerMoveCount += 1; });
    captureButton.addEventListener('click', () => {
      domClickCount += 1;
      if (api.isCapturing) return;
      api.setState('capturing');
      api.setProgress(0, 1, '正在截取整页');
      try {
        if (typeof window.cccRequestCapture === 'function') {
          bindingCallCount += 1;
          window.cccRequestCapture('');
        } else {
          api.setState('error', { message: '截图服务未连接，未保存', detail: '请回到关单核验台重新打开核验。' });
        }
      }
      catch (error) { api.setState('error', { message: '无法请求截图', detail: String(error) }); }
    });
    openLink.addEventListener('click', () => {
      try { if (typeof window.cccOpenFolder === 'function') window.cccOpenFolder(''); } catch (error) { /* ignore */ }
    });

    collapse.addEventListener('click', () => setCollapsed(!collapsed));

    (() => {
      let startX = 0, startY = 0, originLeft = 0, originTop = 0, dragging = false;
      const head = widget.querySelector('.ccc-head');
      head.addEventListener('mousedown', event => {
        if (event.target.closest('button')) return;
        dragging = true;
        const rect = widget.getBoundingClientRect();
        originLeft = rect.left; originTop = rect.top;
        startX = event.clientX; startY = event.clientY;
        host.style.right = 'auto'; host.style.bottom = 'auto';
        host.style.left = originLeft + 'px'; host.style.top = originTop + 'px';
        event.preventDefault();
      });
      window.addEventListener('mousemove', event => {
        if (!dragging) return;
        host.style.left = Math.max(0, originLeft + event.clientX - startX) + 'px';
        host.style.top = Math.max(0, originTop + event.clientY - startY) + 'px';
      });
      window.addEventListener('mouseup', () => { dragging = false; });
    })();

    document.documentElement.appendChild(host);
    window[STATE_KEY] = declarationNo;
    return 'installed';
  }

  if (document.documentElement) return install();
  // The document element is not available yet; install once it is and only then
  // record the installed declaration number (never claim success before mounting).
  return new Promise(resolve => {
    const tryInstall = () => {
      if (!document.documentElement) return false;
      resolve(install());
      return true;
    };
    if (tryInstall()) return;
    document.addEventListener('DOMContentLoaded', () => { if (!tryInstall()) resolve('deferred'); }, { once: true });
    const timer = setInterval(() => { if (tryInstall()) clearInterval(timer); }, 20);
    setTimeout(() => { clearInterval(timer); resolve('timeout'); }, 4000);
  });
})()
