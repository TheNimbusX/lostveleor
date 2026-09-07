// Макет проверяет совместимость обоих предметов до обмена, чтобы ничего не терять.
const panel=document.getElementById('panel');
const portrait=document.createElement('img');portrait.className='portrait';portrait.src='../../../characters/pelag/a pose.png';portrait.alt='Пелаг';panel.prepend(portrait);
const closeButton=document.createElement('button');closeButton.className='close-button';closeButton.textContent='×';closeButton.setAttribute('aria-label','Закрыть сумку');closeButton.onclick=toggle;panel.append(closeButton);
const baseRender=render;
render=function(){baseRender();document.querySelectorAll('[data-i],[data-slot]').forEach(el=>{const slot=el.dataset.slot;const v=slot===undefined?bag[+el.dataset.i]:worn[+slot];el.draggable=!!v;if(slot!==undefined)el.dataset.label=labels[+slot];});};
const locationOf=el=>el.dataset.slot===undefined?{kind:'bag',index:+el.dataset.i}:{kind:'worn',index:+el.dataset.slot};
const collection=loc=>loc.kind==='bag'?bag:worn;
function canMove(from,to){const a=collection(from)[from.index],b=collection(to)[to.index];return !!a&&(to.kind!=='worn'||a.type===to.index)&&(from.kind!=='worn'||!b||b.type===from.index);}
function moveItem(from,to){if(!canMove(from,to))return false;const a=collection(from),b=collection(to);[a[from.index],b[to.index]]=[b[to.index],a[from.index]];selected=to.kind==='bag'?to.index:-1;if(to.kind==='worn')selectedSlot=to.index;return true;}
let dragSource=null;
panel.addEventListener('dragstart',e=>{const el=e.target.closest('[data-i],[data-slot]');if(!el)return;dragSource=locationOf(el);e.dataTransfer.setData('text/plain','camp-item');e.dataTransfer.effectAllowed='move';el.classList.add('dragging');});
panel.addEventListener('dragover',e=>{const el=e.target.closest('[data-i],[data-slot]');if(!el||!dragSource)return;e.preventDefault();const allowed=canMove(dragSource,locationOf(el));e.dataTransfer.dropEffect=allowed?'move':'none';el.classList.toggle('drag-ok',allowed);el.classList.toggle('drag-bad',!allowed);});
panel.addEventListener('dragleave',e=>{const el=e.target.closest('[data-i],[data-slot]');if(el)el.classList.remove('drag-ok','drag-bad');});
panel.addEventListener('drop',e=>{const el=e.target.closest('[data-i],[data-slot]');if(!el||!dragSource)return;e.preventDefault();const ok=moveItem(dragSource,locationOf(el));document.getElementById('notice').textContent=ok?'Предмет перемещён':'Этот предмет не подходит для слота';dragSource=null;render();});
panel.addEventListener('dragend',()=>{dragSource=null;document.querySelectorAll('.drag-ok,.drag-bad,.dragging').forEach(el=>el.classList.remove('drag-ok','drag-bad','dragging'));});
document.getElementById('notice').textContent='Перетащите предмет · двойной клик — надеть';render();
