const tabs = [...document.querySelectorAll('.tab')];
const views = [...document.querySelectorAll('.view')];

function showView(name) {
  tabs.forEach(tab => tab.classList.toggle('is-active', tab.dataset.view === name));
  views.forEach(view => view.classList.toggle('is-active', view.dataset.panel === name));
  const url = new URL(location.href);
  url.searchParams.set('view', name);
  history.replaceState({}, '', url);
}

tabs.forEach(tab => tab.addEventListener('click', () => showView(tab.dataset.view)));
const initial = new URL(location.href).searchParams.get('view');
if (['menu', 'flow', 'icons', 'ocr'].includes(initial)) showView(initial);

const grid = document.querySelector('#copy-grid');
const menu = document.querySelector('#context-menu');
const copyToast = document.querySelector('#copy-toast');
grid?.addEventListener('contextmenu', event => {
  event.preventDefault();
  const rect = grid.getBoundingClientRect();
  menu.style.left = `${Math.min(event.clientX - rect.left, rect.width - 250)}px`;
  menu.style.top = `${Math.min(event.clientY - rect.top, rect.height - 60)}px`;
});
menu?.querySelector('button')?.addEventListener('click', () => {
  copyToast.classList.add('show');
  setTimeout(() => copyToast.classList.remove('show'), 1500);
});

const flowPlay = document.querySelector('#flow-play');
const steps = [...document.querySelectorAll('#flow-steps li')];
const monitorTitle = document.querySelector('#monitor-title');
const monitorDetail = document.querySelector('#monitor-detail');
const monitorProgress = document.querySelector('#monitor-progress');
const monitorCard = document.querySelector('#monitor-card');
const successToast = document.querySelector('#success-toast');
let timers = [];

function clearTimers(){ timers.forEach(clearTimeout); timers = []; }
function setStage(index, title, detail, width) {
  steps.forEach((step, i) => {
    step.classList.toggle('done', i < index);
    step.classList.toggle('active', i === index);
  });
  monitorTitle.textContent = title;
  monitorDetail.textContent = detail;
  monitorProgress.style.width = width;
}

flowPlay?.addEventListener('click', () => {
  clearTimers();
  successToast.classList.remove('show');
  monitorCard.style.display = 'grid';
  setStage(1, '等待人工验证码', '完成验证码并点击网页中的“查询”即可', '42%');
  timers.push(setTimeout(() => setStage(2, '已检测到查询结果', '正在等待页面内容稳定…', '72%'), 1100));
  timers.push(setTimeout(() => setStage(3, '正在生成长截图', '展开网页滚动区域并保存', '92%'), 2200));
  timers.push(setTimeout(() => {
    steps.forEach(step => { step.classList.add('done'); step.classList.remove('active'); });
    monitorCard.style.display = 'none';
    successToast.classList.add('show');
  }, 3300));
});
