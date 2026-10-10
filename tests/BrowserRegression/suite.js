(async () => {
 const no='310120260000000001', other='310120260000000002';
 const monitor=(await (await fetch('../../src/CustomsClearanceConsole/BrowserScripts/monitor.js')).text()).replace('__DECLARATION_NO__',JSON.stringify(no));
 const probe=await (await fetch('../../src/CustomsClearanceConsole/BrowserScripts/probe.js')).text();
 const cases=[
  ['click','ready'],['submit','ready'],['wrong-query','mismatch'],['changed-input','mismatch'],
  ['missing-number','query'],['wrong-result','mismatch'],['stale','query'],['no-query','waiting'],
  ['loading','loading'],['error','error'],['header-only','query'],['multiple-numbers','query'],['wrong-before-query','mismatch']
 ];
 const results=[];
 window.addEventListener('message',e=>{
  if(!e.data?.regression || !cases.some(c=>c[0]===e.data.name))return;
  if(results.some(x=>x.name===e.data.name))return;
  results.push(e.data); document.querySelector('#results').textContent=results.map(r=>`${r.pass?'PASS':'FAIL'}: ${r.name} → ${r.actual} (expected ${r.expected})`).join('\n')+`\n${results.length}/${cases.length}`;
  if(results.length===cases.length) { window.regressionResults=results; document.body.dataset.result=results.every(r=>r.pass)?'passed':'failed'; }
 });
 for(const [name,expected] of cases){
  const iframe=document.createElement('iframe'); iframe.title=name;
  const prepare=`
    const name=${JSON.stringify(name)}, no=${JSON.stringify(no)}, other=${JSON.stringify(other)};
    const input=document.querySelector('input');
    const result=document.querySelector('tbody');
    const resultText='报关单号 '+no+' 申报日期 2026-09-09 放行日期 2026-09-09 海关状态 已放行';
    if(name==='stale')result.innerHTML='<tr><td>'+resultText+'</td></tr>';
    ${monitor};
    if(name==='wrong-query'||name==='wrong-before-query')input.value=other;
    if(!['no-query','wrong-before-query'].includes(name)) {
      if(name==='submit') document.querySelector('form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));
      else document.querySelector('button').click();
    }
    if(name==='changed-input')input.value=other;
    if(name!=='stale')result.innerHTML='<tr><td>'+resultText+'</td></tr>';
    if(name==='missing-number'||name==='header-only')result.innerHTML='<tr><td>申报日期 2026-09-09 放行日期 2026-09-09 海关状态 已放行，查询结果已完整显示</td></tr>';
    if(name==='header-only')document.querySelector('header').textContent='报关单号 '+no;
    if(name==='wrong-result')result.innerHTML='<tr><td>'+resultText.replace(no,other)+'</td></tr>';
    if(name==='multiple-numbers')result.innerHTML='<tr><td>'+resultText+' '+other+'</td></tr>';
    if(name==='loading')document.querySelector('#loading').hidden=false;
    if(name==='error')document.querySelector('#error').textContent='验证码错误';
    setTimeout(()=>{ const actual=(${probe.trim().replace(/;$/,'')}).split('|')[0]; parent.postMessage({regression:true,name,expected:${JSON.stringify(expected)},actual,pass:actual===${JSON.stringify(expected)}},'*'); },1100);
  `;
  iframe.srcdoc='<html><meta charset="utf-8"><header></header><form onsubmit="return false"><label>报关单号 <input data-customs-declaration value="'+no+'"></label><button type="button">查询</button></form><div id="loading" class="loading" hidden>加载中</div><div id="error"></div><table><tbody></tbody></table><script>'+prepare+'</scr'+'ipt></html>';
  document.querySelector('#fixtures').append(iframe);
 }
})().catch(e=>{document.querySelector('#results').textContent=String(e);document.body.dataset.result='failed';});
