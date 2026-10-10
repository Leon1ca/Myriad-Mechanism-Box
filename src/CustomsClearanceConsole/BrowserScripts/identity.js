// Verifies that the page result belongs to the session declaration number.
// Only the real result region counts: the page header, navigation and the capture
// card can never satisfy the check. Errors, loading and unchanged (stale) results
// are reported so a failed query cannot be saved as a false success.
(() => {
  const no = __DECLARATION_NO__;
  const visible = e => !!e && !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
  const inWidget = e => !!(e && (e.id === 'ccc-widget-host' || (e.closest && e.closest('#ccc-widget-host'))));
  const inChrome = e => !!(e && e.closest && e.closest('header,nav,footer,.ccc-widget'));
  const state = window.__customsConsoleMonitor;
  const input = document.querySelector('input[data-customs-declaration]') ||
    [...document.querySelectorAll('input')].find(i => /报关单号|declaration|customs/i.test([i.placeholder, i.name, i.id, i.getAttribute('aria-label')].filter(Boolean).join(' ')));
  const inputValue = (input && input.value || '').trim();
  if (state && state.queryAt && state.queryNo !== no) return 'mismatch|query-number';
  if (inputValue && inputValue !== no) return 'mismatch|input-number';
  if (!state || !state.queryAt) return 'waiting|not-started';

  const bodyText = (document.body ? document.body.innerText : '').replace(/\s+/g, ' ').trim();
  const compact = bodyText.replace(/\s+/g, '');
  const errors = ['验证码错误', '验证码不正确', '验证码无效', '验证码输入错误', '请输入验证码', '查询失败', '未查询到', '没有查询到', '没有符合条件的数据', '暂无数据', '请求失败'];
  const error = errors.find(value => compact.includes(value));
  if (error) return 'error|' + error;
  const loading = [...document.querySelectorAll('[aria-busy=true],.loading,.is-loading,.el-loading-mask,.ant-spin-spinning,.layui-layer-loading')].some(visible);
  if (loading) return 'loading|loading';

  // The result-region definition is injected from one production constant so identity,
  // verify and probe can never drift apart. It includes the official
  // #queryDetail .display-content .content-field > .field-order renderer.
  const resultSelectors = __RESULT_SELECTORS__;
  const resultElements = [...document.querySelectorAll(resultSelectors)]
    .filter(visible).filter(e => !inWidget(e)).filter(e => !inChrome(e)).filter(e => (e.innerText || '').trim().length >= 8);
  if (resultElements.length === 0) return 'waiting|no-result-element';
  const resultText = resultElements.map(e => e.innerText || '').join(' ');
  const numbers = [...new Set(resultText.match(/(?<![0-9])[0-9]{18}(?![0-9])/g) || [])];
  if (numbers.length === 0) return 'waiting|no-result-number';
  if (!numbers.includes(no)) return 'mismatch|result-number';
  if (numbers.length > 1) return 'mismatch|multiple-numbers';

  const hash = value => { let h = 2166136261; for (let i = 0; i < value.length; i++) h = Math.imul(h ^ value.charCodeAt(i), 16777619); return (h >>> 0).toString(16); };
  const current = { length: bodyText.length, height: document.documentElement.scrollHeight, hash: hash(bodyText.slice(-4000)) };
  const baseline = state.baseline || { length: 0, height: 0, hash: '' };
  const changed = current.hash !== baseline.hash &&
    (Math.abs(current.length - baseline.length) >= 24 || Math.abs(current.height - baseline.height) >= 60);
  if (!changed) return 'stale|unchanged';
  return 'ready|' + current.length + ':' + current.height + ':' + current.hash;
})()
