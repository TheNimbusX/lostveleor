"""Build the local motion-reference review with a decoder-independent fallback."""
from pathlib import Path
from html import escape
import json

ROOT = Path(__file__).resolve().parent
OUT = ROOT/'review'/'motion_references'
requests = json.loads((ROOT/'references'/'requests.json').read_text(encoding='utf8'))
jobs = json.loads((ROOT/'references'/'jobs.json').read_text(encoding='utf8'))
notes_path = ROOT/'references'/'review_notes.json'
notes = json.loads(notes_path.read_text(encoding='utf8')) if notes_path.exists() else {}
cards = []
for spec in requests['requests']:
    job = next((j for j in jobs if j['slug'] == spec['slug']), {})
    if job.get('status') != 'completed':
        continue
    slug, title = spec['slug'], escape(spec['title'])
    count, fps = job['preview_frames'], job['preview_fps']
    note = notes.get(slug, {})
    observations = ''.join('<li>'+escape(s)+'</li>' for s in note.get('observations', []))
    cards.append(f'''<article id="{slug}" data-slug="{slug}" data-count="{count}" data-fps="{fps}">
<div class="heading"><span class="number">0{spec['index']}</span><div><h2>{title}</h2><p>{escape(note.get('status', 'На проверке'))}</p></div></div>
<video src="{slug}.webm" poster="../../references/{slug}_start.jpg" controls muted loop playsinline preload="metadata"></video>
<img class="frames" src="{slug}_frames/0001.jpg" alt="Кадры: {title}" hidden>
<div class="controls"><button class="play">Смотреть</button><button class="previous" aria-label="Предыдущий кадр">− кадр</button><button class="next" aria-label="Следующий кадр">+ кадр</button><input type="range" min="0" max="{count-1}" value="0" aria-label="Позиция просмотра"><output>0.00 с</output><select aria-label="Скорость"><option value="1">Обычная скорость</option><option value="0.5">Вдвое медленнее</option><option value="0.25">Вчетверо медленнее</option></select><button class="fallback">Показать кадрами</button></div>
<div class="notes"><p>{escape(note.get('description', 'Референс движения для согласования.'))}</p><ul>{observations}</ul><div class="links"><a href="../../references/{slug}.mp4">Исходное видео MP4</a><a href="{slug}_sheet.jpg">Ключевые кадры</a><a href="../../references/{slug}_start.jpg">Модель, переданная в Higgsfield</a></div></div></article>''')
page = '''<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Камнекопыт · Референсы движений</title>
<style>
:root{color-scheme:dark;--bg:#141b19;--panel:#202925;--line:#3b473e;--ink:#eae9df;--muted:#b3beb1;--accent:#cad69d}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.55 'Segoe UI',sans-serif}main{max-width:1150px;margin:auto;padding:35px 25px 70px}h1{font-size:40px;line-height:1.15;margin:12px 0}h2{font-size:24px;margin:0}p{color:var(--muted);margin:8px 0}.eyebrow{font-size:12px;letter-spacing:.12em;color:var(--accent)}nav,.links{display:flex;gap:18px;flex-wrap:wrap;margin:20px 0}a{color:var(--accent)}article{background:var(--panel);border:1px solid var(--line);border-radius:14px;margin-top:28px;overflow:hidden;scroll-margin-top:15px}.heading{display:flex;gap:18px;padding:20px 24px;align-items:center}.number{color:var(--accent);font-size:30px}video,.frames{display:block;width:100%;aspect-ratio:16/9;object-fit:contain;background:#8c938e}.controls{display:flex;gap:8px;align-items:center;flex-wrap:wrap;padding:16px 22px}button,select{font:inherit;color:var(--ink);background:#29342d;border:1px solid #506052;border-radius:6px;padding:7px 10px;cursor:pointer}button:hover{border-color:var(--accent)}input{accent-color:var(--accent);flex:1;min-width:130px}output{font-size:14px;min-width:62px;font-variant-numeric:tabular-nums}.notes{padding:0 24px 18px}.notes ul{padding-left:22px;color:#dce1d5}.callout{border-left:3px solid #b7a672;background:#2e3025;padding:12px 18px;margin:24px 0}.callout p{color:#e1dcc7}.links{font-size:14px}.footer{margin-top:28px;font-size:14px}[hidden]{display:none!important}@media(max-width:700px){main{padding:20px 12px}h1{font-size:30px}h2{font-size:20px}.controls{padding:12px}.heading,.notes{padding-left:16px;padding-right:16px}}
</style><main><div class="eyebrow">THE WAR REMAINS · КАМНЕКОПЫТ · ЭТАП 02</div><h1>Как он движется</h1><p>Три видеореференса Higgsfield на основе принятой модели. По ним выбираем характер движения перед работой с ригом в Blender.</p><nav><a href="#charge">Подготовка и разбег</a><a href="#collision">Столкновение</a><a href="#death">Смерть</a><a href="../model_stage/index.html">Принятая модель</a></nav>
<div class="callout"><p>Референсы приняты владельцем 25 сентября. <a href="../animation_windup_start/index.html">Первый клип на настоящей модели: подготовка и старт →</a></p><p>Замечания к генерации ниже сохранены для постановки на риге: два скребка перед стартом, устойчивые опоры и отсутствие изменения анатомии.</p></div>
''' + '\n'.join(cards) + '''<p class="footer">Следующий этап после согласования: подготовка и старт на настоящем риге. Далее сдаём клипы по одному. В Unity эти видео не импортировались.</p></main><script>
document.querySelectorAll('article[data-slug]').forEach(card=>{
  const video=card.querySelector('video'),image=card.querySelector('.frames'),range=card.querySelector('input'),out=card.querySelector('output');
  const play=card.querySelector('.play'),mode=card.querySelector('.fallback'),select=card.querySelector('select');
  const count=Number(card.dataset.count),fps=Number(card.dataset.fps),slug=card.dataset.slug;
  let fallback=false,running=false,frame=0,started=0,first=0,last=-1,speed=1;
  function draw(){if(fallback&&last!==frame){image.src=`${slug}_frames/${String(frame+1).padStart(4,'0')}.jpg`;last=frame;}range.value=frame;out.value=`${(frame/fps).toFixed(2)} с`;play.textContent=(fallback?running:!video.paused)?'Пауза':'Смотреть';}
  function frames(auto=false){video.pause();video.hidden=true;image.hidden=false;fallback=true;running=auto;first=frame;started=performance.now();mode.textContent='Просмотр кадрами';mode.disabled=true;draw();}
  function seek(value){video.pause();running=false;frame=Math.max(0,Math.min(count-1,value));if(!fallback&&video.readyState>0)video.currentTime=Math.min((frame+.01)/fps,Math.max(0,video.duration-.001));draw();}
  play.onclick=()=>{if(fallback){running=!running;first=frame;started=performance.now();draw();}else if(video.paused)video.play().catch(()=>frames(true));else video.pause();};
  mode.onclick=()=>frames();video.addEventListener('error',()=>frames());range.oninput=()=>seek(Number(range.value));
  card.querySelector('.previous').onclick=()=>seek(frame-1);card.querySelector('.next').onclick=()=>seek(frame+1);
  select.onchange=()=>{speed=Number(select.value);video.playbackRate=speed;first=frame;started=performance.now();};
  function update(now){if(fallback&&running)frame=(first+Math.floor((now-started)*fps*speed/1000))%count;else if(!fallback)frame=Math.max(0,Math.min(count-1,Math.floor(video.currentTime*fps)));draw();requestAnimationFrame(update);}
  requestAnimationFrame(update);if(video.error)frames();
});
</script></html>'''
(OUT/'index.html').write_text(page, encoding='utf8')
print('Review page:', len(cards), 'references')
