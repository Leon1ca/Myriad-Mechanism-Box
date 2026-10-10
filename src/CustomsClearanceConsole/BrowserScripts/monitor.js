(() => {
          const no = __DECLARATION_NO__;
          // A frame's initial about:blank document hands its Window object to the same-origin
          // document that replaces it, so a monitor found on window can still be listening on the
          // discarded document. It only counts when it is bound to the current document.
          const existing = window.__customsConsoleMonitor;
          if (existing?.declarationNo === no && existing.document === document) return 'monitoring';
          const visible = e => !!e && !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
          const text = () => (document.body?.innerText || '').replace(/\s+/g, ' ').trim();
          const hash = value => { let h = 2166136261; for (let i = 0; i < value.length; i++) h = Math.imul(h ^ value.charCodeAt(i), 16777619); return (h >>> 0).toString(16); };
          // documentElement/body may not exist yet when this runs at document start;
          // never throw, so the rest of the shell (the card) still installs.
          const signature = () => { const root = document.documentElement; const value = text(); return { length:value.length, height:root ? root.scrollHeight : 0, hash:hash(value.slice(-3000)) }; };
          const state = window.__customsConsoleMonitor = { declarationNo:no, queryAt:0, baseline:signature(), document };
          const isQueryControl = target => {
            const control = target?.closest?.('button,input[type=button],input[type=submit],a,[role=button]');
            if (!visible(control)) return false;
            const label = (control.innerText || control.value || control.getAttribute('aria-label') || '').replace(/\s+/g, '');
            return /^查询$/.test(label) || /查询报关单|开始查询/.test(label);
          };
          const beginQuery = () => {
            const input = document.querySelector('input[data-customs-declaration]') || [...document.querySelectorAll('input')].find(i => /报关单号|declaration|customs/i.test([i.placeholder,i.name,i.id,i.getAttribute('aria-label')].join(' ')));
            state.queryNo = (input?.value || '').trim(); state.baseline = signature(); state.queryAt = Date.now();
          };
          // click covers keyboard activation as well as pointer input, without double-resetting the baseline.
          document.addEventListener('click', event => { if (isQueryControl(event.target)) beginQuery(); }, true);
          document.addEventListener('submit', beginQuery, true);
          return 'monitoring';
        })()
