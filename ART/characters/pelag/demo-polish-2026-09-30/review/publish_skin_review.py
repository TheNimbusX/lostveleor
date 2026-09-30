"""Publish unmodified gameplay captures of the owner's mounting and skin revision."""
from pathlib import Path
import html

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'review'
old = OUT / 'index-r02.html'
current = OUT / 'index.html'
if current.exists() and not old.exists():
    old.write_bytes(current.read_bytes())

views = [
    ('Кожа и силуэт в лесном бою', 'close-front/shot_00_t0.20s.png'),
    ('Якорь сзади', 'forest-front/shot_01_t1.00s.png'),
    ('Лагерь, штатное освещение', 'camp/shot_01_t1.00s.png'),
    ('Спереди в тени', 'forest-back/shot_01_t1.00s.png'),
]
figures = ''.join(f'<figure><figcaption>{html.escape(label)}</figcaption><a href="../colour/skin-r03/{file}"><img src="../colour/skin-r03/{file}" alt="{html.escape(label)}"></a></figure>' for label, file in views)
page = '''<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Пелаг — посадка якоря и кожа</title><style>
*{box-sizing:border-box}body{margin:0;background:#eee8df;color:#302c29;font:17px/1.5 system-ui,sans-serif}main{max-width:1240px;margin:auto;padding:24px}h1{font-size:30px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}figure{margin:0}figcaption{margin:10px 0}img,video{width:100%;display:block;border-radius:6px}section{padding:20px;margin:22px 0;background:#fffaf2;border:1px solid #dbd0bf;border-radius:8px}a{color:#985347}.small{font-size:14px;color:#71685c}@media(max-width:720px){.grid{grid-template-columns:1fr}main{padding:12px}}
</style><main><p class="small">30 сентября 2026 · Правки по скриншотам владельца · Ревизия 03</p>
<h1>Посадка якоря и кожа Пелага</h1>
<p>Якорь закреплён по твоим значениям из инспектора. Кожа светлее и менее жёлтая; коррекция отделена от одежды, волос, наручей и металлических деталей. Геометрия героя сохранена.</p>
<p>Настройки сохранены в профиле внешности и материале. Проверка — в обычном Play или <b>Разлом → Пелаг → Темп боя</b>. Ручная настройка — <b>Разлом → Пелаг → Внешность → Настроить посадку и цвет</b>.</p>
<section><div class="grid">''' + figures + '''</div></section>
<section><h2>Отмена и возврат якоря</h2><video src="../colour/skin-r03/roll/pelag_anchor-slam_1080p60.mp4" controls playsinline preload="metadata"></video><p class="small">Запись из игры: 960 × 540, 60 кадров/с. Максимальное растяжение сегмента цепи — 0,78%; возврат к креплению зарегистрирован. Анимации и эффекты здесь существующие.</p></section>
<p><a href="index-r02.html">Предыдущая ревизия и исходники якоря</a> · <a href="../colour/skin-r03/verification.json">Проверка этой ревизии</a></p>
<p class="small">Сборка прошла. Кадры сняты в отдельной тестовой версии. Визуальную приёмку подтверждает владелец.</p></main></html>'''
current.write_text(page, encoding='utf-8')
print('Published mounting and skin revision 03')
