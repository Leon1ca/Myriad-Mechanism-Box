(() => {
          const state = window.__customsConsoleMonitor;
          if (!state) return 'waiting|not-started';
          const visible = e => !!e && !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
          const input = document.querySelector('input[data-customs-declaration]') || [...document.querySelectorAll('input')].find(i => /报关单号|declaration|customs/i.test([i.placeholder,i.name,i.id,i.getAttribute('aria-label')].join(' ')));
          if ((state.queryAt && state.queryNo !== state.declarationNo) || (input && input.value.trim() !== state.declarationNo)) return 'mismatch|declaration-number';
          if (!state.queryAt) return 'waiting|not-started';
          const bodyText = (document.body?.innerText || '').replace(/\s+/g, ' ').trim();
          const compact = bodyText.replace(/\s+/g, '');
          const errors = ['验证码错误','验证码不正确','验证码无效','验证码输入错误','请输入验证码','查询失败','未查询到','没有查询到','没有符合条件的数据','暂无数据','请求失败'];
          if (errors.some(value => compact.includes(value))) return 'error|' + errors.find(value => compact.includes(value));
          const loading = [...document.querySelectorAll('[aria-busy=true],.loading,.is-loading,.el-loading-mask,.ant-spin-spinning,.layui-layer-loading')].some(visible);
          if (loading) return 'loading|' + document.documentElement.scrollHeight;
          const hash = value => { let h = 2166136261; for (let i = 0; i < value.length; i++) h = Math.imul(h ^ value.charCodeAt(i), 16777619); return (h >>> 0).toString(16); };
          const current = { length:bodyText.length, height:document.documentElement.scrollHeight, hash:hash(bodyText.slice(-3000)) };
          const changed = current.hash !== state.baseline.hash &&
            (Math.abs(current.length - state.baseline.length) >= 24 || Math.abs(current.height - state.baseline.height) >= 60);
          const resultMarkers = ['申报日期','放行日期','结关日期','海关状态','通关状态','查验状态','申报海关','放行','结关'];
          const markerCount = resultMarkers.filter(value => compact.includes(value)).length;
          const resultElements = [...document.querySelectorAll(__RESULT_SELECTORS__)]
            .filter(visible).filter(el => (el.innerText || '').trim().length >= 8);
          const resultRows = resultElements.length;
          const elapsed = Date.now() - state.queryAt;
          const fingerprint = `${current.length}:${current.height}:${current.hash}:${resultRows}`;
          const resultNumbers = resultElements.map(el => el.innerText || '').join(' ').match(/(?<![0-9])[0-9]{18}(?![0-9])/g) || [];
          if (resultNumbers.length && !resultNumbers.includes(state.declarationNo)) return 'mismatch|result-number';
          const identityMatches = resultNumbers.includes(state.declarationNo) && new Set(resultNumbers).size === 1;
          if (elapsed >= 900 && identityMatches && changed && (markerCount >= 2 || resultRows >= 1)) return 'ready|' + fingerprint;
          return 'query|' + fingerprint;
        })()
