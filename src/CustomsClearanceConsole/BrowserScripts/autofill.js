(() => {
          const no = __DECLARATION_NO__;
          const visible = e => !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
          const normalize = s => (s || '').replace(/[\s：:＊*]/g, '');
          const roots = [document];
          for (let n = 0; n < roots.length; n++)
            for (const e of roots[n].querySelectorAll('*')) if (e.shadowRoot && !roots.includes(e.shadowRoot)) roots.push(e.shadowRoot);
          const queryAll = selector => roots.flatMap(root => [...root.querySelectorAll(selector)]);
          const labels = queryAll('label,span,td,div')
            .filter(e => visible(e) && e.children.length <= 3 && normalize(e.innerText) === '报关单号');
          const inputs = queryAll('input')
            .filter(i => visible(i) && (!i.type || ['text','tel'].includes(i.type)));
          const distance = (label, input) => {
            const a = label.getBoundingClientRect(), b = input.getBoundingClientRect();
            const vertical = Math.abs((a.top + a.bottom) / 2 - (b.top + b.bottom) / 2);
            const horizontal = b.left >= a.right ? b.left - a.right : Math.abs(b.left - a.left) + 300;
            return vertical * 5 + horizontal;
          };
          let target = null;
          target = inputs.find(i => /报关单号|declaration|customs/i.test([i.placeholder,i.name,i.id,i.getAttribute('aria-label')].filter(Boolean).join(' '))) || null;
          for (const label of labels) {
            if (target && visible(target)) break;
            if (label.htmlFor) target = queryAll('#' + CSS.escape(label.htmlFor))[0] || null;
            if (!target) {
              const row = label.closest('tr,.form-group,.el-form-item,.ant-form-item,.layui-form-item') || label.parentElement;
              target = row?.querySelector('input:not([type=hidden])') || null;
            }
            if (target && visible(target)) break;
          }
          if ((!target || !visible(target)) && labels.length && inputs.length)
            target = inputs.slice().sort((a,b) => distance(labels[0], a) - distance(labels[0], b))[0];
          if (!target) return 'waiting';
          target.setAttribute('data-customs-declaration', 'true');
          target.focus();
          const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
          if (setter) setter.call(target, no); else target.value = no;
          target.setAttribute('value', no);
          for (const type of ['input','change','keyup','blur']) target.dispatchEvent(new Event(type, {bubbles:true, composed:true}));
          target.focus();
          return target.value === no ? 'filled' : 'waiting';
        })()
