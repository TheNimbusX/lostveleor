"""Package final Blender cycle and actual Unity recording; refuses an incomplete take."""
import subprocess,shutil,csv,json
from pathlib import Path
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parent
REPO=ROOT.parents[6]
OUT=ROOT.parent.parent/'review/animation_reference_walk'
WORK=ROOT/'walk_revision_r04'
record=REPO/'artifacts/wendigo-review/editor-walk-r04-60'
ff=imageio_ffmpeg.get_ffmpeg_exe()
def encode(args,destination):
    subprocess.run([ff,'-y',*args,'-c:v','libx264','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(destination)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
rows=list(csv.reader((record/'timing.csv').open()))
frames=[r for r in rows if r[0]=='frame']
assert float(frames[-1][3])>=15.95, 'The Unity recording is not complete'
encode(['-framerate','60','-i',str(WORK/'front/pose_%04d.png'),'-frames:v','48'],OUT/'walk_3d_r04.mp4')
encode(['-framerate','24','-i',str(record/'frame_%05d.png'),'-frames:v',str(len(frames))],OUT/'gameplay_r04_ikfix.mp4')
if not (OUT/'index_r03.html').exists():shutil.copy2(OUT/'index.html',OUT/'index_r03.html')
(OUT/'index.html').write_text('''<!doctype html><html lang="ru"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>Вендиго · походка и темп боя</title>
<style>:root{color-scheme:dark;font:16px/1.5 system-ui;background:#171e1a;color:#e7ece3}body{max-width:1180px;margin:auto;padding:28px}h1{margin:0}h2{font-size:21px}p{color:#bdc9ba}video{width:100%;display:block;background:#111;border-radius:10px}section{margin:26px 0}button,select{font:inherit;background:#314233;color:#eee;border:1px solid #71806a;border-radius:5px;padding:8px 12px}a{color:#d2ddb4}.controls{display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin:12px 0}input{width:100%;accent-color:#ceddb8}.cycle{max-width:680px}small{color:#a9b8a5}</style>
<h1>Вендиго · походка и темп боя</h1>
<p>Исправлена игровая ходьба: при экспорте стопы ошибочно оставались закреплены в позе ожидания. Теперь Unity воспроизводит полный шаг из клипа r04. Ходьба 3 м/с; когти — 45 урона и 1,5 с между началами атак; прыжок — раз в 6,5 с, включая задержку первого.</p>
<section><h2>В игре · исправленный экспорт</h2><video src="gameplay_r04_ikfix.mp4" controls muted loop playsinline preload="metadata"></video>
<p>Настоящий тестовый бой в Unity: преследование, повороты, остановки и переходы в атаки. Герой идёт по контрольному маршруту; бессмертие включено только для записи.</p></section>
<section class="cycle"><h2>Цикл ходьбы крупно</h2><video id="walk" src="walk_3d_r04.mp4" controls muted loop playsinline preload="auto"></video>
<div class="controls"><button id="play">Пуск / пауза</button><button id="prev">← Кадр</button><button id="next">Кадр →</button>
<select id="speed"><option value="1">Игровой темп</option><option value="0.4">Медленно · 24 кадра/с</option><option value="0.2">Очень медленно</option></select><output id="frame"></output></div>
<input id="scrub" type="range" min="0" max="47" step="1" value="0" aria-label="Кадр цикла">
<p>Перенос веса, противоход плеч, запаздывание кистей, опора стоп и перекат. Полный цикл — 2,4 м за 0,8 с. Перемещение по арене остаётся в симуляции.</p></section>
<p>Проверить самому: <b>Unity → Разлом → Лесной вендиго → Тестовый бой</b>.</p>
<p><a href="../../animation/reference_match_remaining/walk_revision_r04/Walk_Final.blend">Рабочая сцена ходьбы</a> · <a href="../animation_unity_package/index.html">Весь набор клипов</a></p>
<details><summary>Прежние варианты для сравнения</summary><p><a href="gameplay_r04_before_ik_fix.mp4">До исправления: стопы почти неподвижны</a> · <a href="index_r03.html">Ходьба r03, отклонённая после игрового теста</a></p></details>
<script>const v=document.querySelector('#walk'),s=document.querySelector('#scrub'),o=document.querySelector('#frame');const seek=f=>{v.pause();v.currentTime=Math.max(0,Math.min(47,f))/60};document.querySelector('#play').onclick=()=>v.paused?v.play():v.pause();document.querySelector('#prev').onclick=()=>seek(Math.round(v.currentTime*60)-1);document.querySelector('#next').onclick=()=>seek(Math.round(v.currentTime*60)+1);s.oninput=()=>seek(+s.value);document.querySelector('#speed').onchange=e=>v.playbackRate=+e.target.value;function loop(){const f=Math.min(47,Math.floor(v.currentTime*60));s.value=f;o.textContent=`Кадр ${f} / 47`;requestAnimationFrame(loop)}loop();</script></html>''',encoding='utf-8')
main=OUT.parent/'animation_unity_package/index.html'
content=main.read_text(encoding='utf-8')
link='<p><a href="../animation_reference_walk/index.html">Обновление: ходьба r04 и новый ритм боя — 25 сентября</a></p>'
if link not in content:content=content.replace('<main>','<main>'+link,1);main.write_text(content,encoding='utf-8')
events=[r for r in rows if r[0]=='event' and r[4]=='WendigoStarted']
(WORK/'gameplay_review.json').write_text(json.dumps({'duration':float(frames[-1][3]),'captured_frames':len(frames),'starts':events},ensure_ascii=False,indent=2))
print(OUT/'index.html')
