"""Собирает просмотр настоящих игровых кадров, не меняя их цвет и обработку."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import json
import shutil

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'ART/CAMP/look-2026-09-13'
OUT.mkdir(parents=True, exist_ok=True)
STYLES = {'Original': 'Исходный', 'Clean': 'Чистый атмосферный',
          'Painterly': 'Мягкий живописный', 'Film': 'Лёгкий плёночный', 'Aces': 'Сравнение ACES'}
SHOTS = [('Общий вид', 'shot_00_t2.00s.png'), ('Костёр', 'shot_01_t4.00s.png'),
         ('Палатки', 'shot_02_t6.00s.png'), ('Река', 'shot_03_t8.00s.png'),
         ('Алтари', 'shot_04_t10.00s.png'), ('Герой', 'shot_05_t19.00s.png')]
for style in STYLES:
    source = ROOT / f'artifacts/camp-look-{style.lower()}-review'
    dest = OUT / style
    dest.mkdir(exist_ok=True)
    for _, name in SHOTS:
        shutil.copy2(source / name, dest / name)
    shutil.copy2(source / 'look-check.txt', dest / 'look-check.txt')
    for movie in source.glob('*.mp4'):
        shutil.copy2(movie, dest / 'walk.mp4')

font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 24)
small = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 21)
poster = Image.new('RGB', (1920, 1245), '#111b24')
draw = ImageDraw.Draw(poster)
for col, style in enumerate(['Clean', 'Painterly', 'Film']):
    draw.text((col * 640 + 18, 16), STYLES[style], font=font, fill='white')
    for row, shot in enumerate([1, 4, 5]):
        label, name = SHOTS[shot]
        y = 58 + row * 395
        frame = Image.open(OUT / style / name)
        frame.thumbnail((640, 360), Image.Resampling.LANCZOS)
        poster.paste(frame, (col * 640, y + 28))
        draw.text((col * 640 + 18, y), label, font=small, fill='#bad1d6')
poster.save(OUT / 'comparison.jpg', quality=94)

html = r'''<!doctype html><html lang="ru"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Лагерь — свет и постобработка</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#101920;color:#eaf0ef;font:16px system-ui,sans-serif}
main{max-width:1560px;margin:auto;padding:26px}h1{font-size:28px;margin:0 0 10px}p{color:#aebfc4;line-height:1.5}
nav{display:flex;gap:10px;flex-wrap:wrap;margin:22px 0}button,select{background:#263941;color:inherit;border:1px solid #58707a;padding:10px 14px;border-radius:7px;font:inherit}
button[aria-pressed=true]{background:#b4d3c8;color:#15251e}.controls{display:flex;gap:20px;align-items:center;flex-wrap:wrap}
label{display:flex;align-items:center;gap:10px}.compare{position:relative;aspect-ratio:16/9;overflow:hidden;background:#000;margin-top:14px}
.compare img{position:absolute;inset:0;width:100%;height:100%;object-fit:contain}.cut{clip-path:inset(0 50% 0 0)}
.tag{position:absolute;top:14px;padding:7px 12px;background:#101920dc;border-radius:5px}.right{right:14px}.left{left:14px}
input{width:min(600px,80vw);accent-color:#b4d3c8}section{margin-top:26px}.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:12px}.grid img,.grid video{width:100%;display:block}h2{font-size:20px}.grid p{margin:8px 0}
@media(max-width:700px){main{padding:14px}.grid{grid-template-columns:1fr}}
</style><main><h1>Лагерь — свет и постобработка</h1>
<p>Игровая сборка · 1920 × 1080 · High · одинаковые ракурсы. Три варианта на общей световой основе. Исходный вид сохранён; ACES вынесен в отдельное сравнение. Выбор окончательного оформления ещё открыт.</p>
<nav id="shots"></nav><div class="controls"><label>Слева <select id="a"></select></label><label>Справа <select id="b"></select></label><label>Граница <input id="split" type="range" min="0" max="100" value="50"></label></div>
<div class="compare"><img id="back" alt="Правый вариант"><img class="cut" id="front" alt="Левый вариант"><span class="tag left" id="la"></span><span class="tag right" id="lb"></span></div>
<section><h2>Три варианта рядом</h2><div class="grid" id="grid"></div></section>
<section><h2>Одинаковый проход героя · 7 секунд</h2><p>Кнопка запускает три записи одновременно. Полные видео доступны в папках вариантов.</p><button id="play">Смотреть одновременно</button><div class="grid" id="videos"></div></section>
<p>Чистый: Neutral, насыщенность +6, контраст +10, bloom 0,18, без зерна и виньетки. Живописный: тёплые света и прохладные приподнятые тени, насыщенность +9, контраст +5, bloom 0,32 / scatter 0,7, виньетка 0,13. Плёночный: прохладный баланс, насыщенность −10, контраст +22, зерно Thin1 0,10, виньетка 0,20. Для всех: тени 0,82, прохладное заполнение 0,085. Motion blur, DOF и хроматическая аберрация выключены. Различия усилены после замечания владельца; стартовые почти незаметные значения пересмотрены.</p>
</main><script>
const styles=STYLES_DATA, shots=SHOTS_DATA;let shot=0;
const el=id=>document.getElementById(id);
for(const id of ['a','b'])for(const [value,name]of Object.entries(styles))el(id).add(new Option(name,value));
el('a').value='Original';el('b').value='Clean';
shots.forEach(([name],i)=>{const b=document.createElement('button');b.textContent=name;b.onclick=()=>{shot=i;render()};el('shots').append(b)});
for(const style of ['Clean','Painterly','Film']){const card=document.createElement('div');card.innerHTML=`<p>${styles[style]}</p><img data-style="${style}" alt="${styles[style]}">`;el('grid').append(card);
const v=document.createElement('div');v.innerHTML=`<p>${styles[style]}</p><video controls muted loop preload="metadata" src="${style}/walk.mp4"></video>`;el('videos').append(v)}
function render(){el('front').src=`${el('a').value}/${shots[shot][1]}`;el('back').src=`${el('b').value}/${shots[shot][1]}`;el('la').textContent=styles[el('a').value];el('lb').textContent=styles[el('b').value];document.querySelectorAll('[data-style]').forEach(i=>i.src=`${i.dataset.style}/${shots[shot][1]}`);[...el('shots').children].forEach((b,i)=>b.setAttribute('aria-pressed',i===shot))}
el('a').onchange=el('b').onchange=render;el('split').oninput=()=>el('front').style.clipPath=`inset(0 ${100-el('split').value}% 0 0)`;
el('play').onclick=()=>document.querySelectorAll('video').forEach(v=>{v.currentTime=0;v.play()});render();
</script></html>'''
html = html.replace('STYLES_DATA', json.dumps(STYLES, ensure_ascii=False)).replace('SHOTS_DATA', json.dumps(SHOTS, ensure_ascii=False))
(OUT / 'index.html').write_text(html, encoding='utf-8')
print(OUT)
