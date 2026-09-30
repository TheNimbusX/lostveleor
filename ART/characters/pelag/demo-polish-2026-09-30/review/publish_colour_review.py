"""Publish the current colour/anchor review using actual capture outputs."""
from pathlib import Path
from html import escape
import json
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
test_report = ET.parse(ROOT.parents[3] / "artifacts/pelag-colour-checks/presentation-final.trx")
test_counts = test_report.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert test_counts['failed'] == '0'
sections = []
for title, name in [("Лагерь", "camp"), ("Лес", "forest")]:
    cards = []
    for label, suffix in [("До", "before"), ("После", "after")]:
        shots = sorted((ROOT / "colour/game" / f"{name}-{suffix}").glob("shot_*.png"))
        if shots:
            url = "../" + shots[0].relative_to(ROOT).as_posix()
            cards.append(f'<figure><figcaption>{label}</figcaption><a href="{url}" target="_blank"><img src="{url}" alt="{title}: {label}"></a></figure>')
    if cards:
        size = '3,5' if name == 'camp' else '2,5'
        sections.append(f'<section><h2>{title} · настоящий игровой кадр</h2><div class="grid">{"".join(cards)}</div><p class="small">Одна сборка, одинаковые камера и освещение. Камера приближена для осмотра (размер {size}); настройка обычной камеры игры не менялась. Для «до» отключён только профиль новой внешности.</p></section>')

videos = []
for folder, title in [("run-after", "Бег и развороты"), ("basic-attack-handoff-r02", "Обычный удар → якорь"), ("basic-roll-r02", "Кувырок с якорем за спиной"), ("roll-after", "Отмена удара кувырком"),
                      ("slam-after", "Удар якорем и возврат"), ("leap-after", "Абордаж"),
                      ("wreck-after", "Крушение"), ("death-after", "Смерть")]:
    category = "game-verified" if folder in ("slam-after", "roll-after", "death-after", "basic-attack-handoff-r02") else "game"
    for movie in sorted((ROOT / "colour" / category / folder).glob("*.mp4")):
        url = "../" + movie.relative_to(ROOT).as_posix()
        videos.append(f'<figure><figcaption>{title}</figcaption><video src="{url}" controls playsinline preload="metadata"></video></figure>')

extra_videos = []
for folder, title in [("whirlwind", "Вихрь"), ("cleave", "Рассекающий"), ("blaze", "Ладно смазал"),
                      ("chain-step", "Шквал"), ("fire-flask", "Взрывная смесь"),
                      ("skewer", "На вылет"), ("backblast", "Отбой")]:
    for movie in sorted((ROOT / "colour/poses-r02" / folder).glob("*.mp4")):
        url = "../" + movie.relative_to(ROOT).as_posix()
        extra_videos.append(f'<figure><figcaption>{title}</figcaption><video src="{url}" controls playsinline preload="metadata"></video></figure>')

gallery = "".join(f'<figure><figcaption>{label}</figcaption><a href="../colour/{name}.png" target="_blank"><img src="../colour/{name}.png" alt="{label}"></a></figure>'
                  for label, name in [("Спереди", "after-front"), ("Сзади", "after-back"), ("Сбоку", "after-side")])
page = '''<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Пелаг — цвет и новый якорь</title><style>
*{box-sizing:border-box}body{margin:0;background:#ece8df;color:#302e29;font:17px/1.55 system-ui,sans-serif}main{max-width:1240px;margin:auto;padding:26px}h1{font-size:30px;line-height:1.2}h2{font-size:22px}section{margin:28px 0;padding:20px;background:#fffaf1;border-radius:10px;border:1px solid #d7cdbc}p{max-width:880px}.small{font-size:14px;color:#72685b}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.three{grid-template-columns:repeat(3,minmax(0,1fr))}figure{margin:0}figcaption{margin:8px 0}img,video{display:block;width:100%;border-radius:5px}a{color:#a0454b}code{overflow-wrap:anywhere}details{margin-top:16px}@media(max-width:700px){.grid,.three{grid-template-columns:1fr}main{padding:12px}}
</style><main><p class="small">30 сентября 2026 · Кандидат на проверку владельцем</p><h1>Пелаг — цвет и новый якорь</h1>
<p>Насыщеннее коралловый и бирюзовый, яснее складки светлой ткани. Новый тяжёлый якорь расположен диагонально за спиной. Нынешние лицо, волосы, костюм, скелет и сабля сохранены.</p>
<p>Проверяй в обычном Play. Для боя есть <b>Разлом → Пелаг → Темп боя</b>. Для ручной настройки: <b>Разлом → Пелаг → Внешность → Настроить посадку и цвет</b>. Здесь доступны крепления и два материала; они остаются редактируемыми.</p>
''' + "".join(sections) + '<section><h2>Посадка оружия крупно</h2><p class="small">Предварительный просмотр Unity на действующем риге. Игровые кадры — выше.</p><div class="grid three">' + gallery + '</div></section>'
if videos:
    page += '<section><h2>Проверка в движении</h2><p>Проверяем новую модель и крепления. Это существующие анимации и эффекты, их художественная доработка сюда не входит.</p><div class="grid">' + "".join(videos) + '</div></section>'
if extra_videos:
    page += '<section><details><summary>Другие способности — записи проверки посадки</summary><p class="small">Это технические прогоны без переделки способностей. Запись начинается до применения. В некоторых ракурсах героя закрывает растительность; это не художественная приёмка клипов или эффектов.</p><div class="grid">' + "".join(extra_videos) + '</div></details></section>'
page += '''<section><h2>Новый якорь</h2><div class="grid"><figure><figcaption>Исходный арт Imagegen</figcaption><img src="../anchor-imagegen-tripo/anchor-concept.png" alt="Арт якоря"></figure><figure><figcaption>Модель из Tripo · 5 872 треугольника</figcaption><img src="../anchor-imagegen-tripo/anchor-three-quarter.png" alt="Модель якоря"></figure></div><p class="small">Один оплаченный image-to-model: 30 кредитов. Повторных генераций не было. Техническая проверка не означает визуального принятия владельцем.</p><details><summary>Исходники и проверка</summary><p><a href="../anchor-imagegen-tripo/Pelag_Anchor_Tripo_Work.blend">Редактируемый Blender-исходник</a> · <a href="../anchor-imagegen-tripo/export_anchor.py">Рецепт экспорта</a> · <a href="../anchor-imagegen-tripo/manifest.json">Происхождение и стоимость</a> · <a href="../colour/verification.json">Замеры и ограничения проверки</a></p><p>237/237 тестов представления. Unity-сборка успешна. В снятых кувырке и «Крушении» при 30/60/120 кадрах/с растяжение цепи менее 0,8%, ошибка её крепления в замере — 0. Исходный FBX Пелага и хват сабли сохранены; новые материалы и якорь подключены отдельным профилем.</p><p>Весь герой ещё не оптимизирован до 40 тысяч треугольников: тело и прежняя сабля сохранены. Здесь проверена модель нового якоря и её подключение; измерение на слабом ПК не выполнялось.</p></details></section></main></html>'''
page = page.replace('237/237 тестов представления',
                    f"{test_counts['passed']}/{test_counts['total']} тестов представления")
page = page.replace('растяжение цепи менее 0,8%', 'растяжение цепи не превышает 2%')
page = page.replace('измерение на слабом ПК не выполнялось.',
                    'измерение на слабом ПК не выполнялось. При смерти оружие ещё освобождается мгновенно; плавность этого перехода требует отдельной анимационной доработки.')
(ROOT / "review/index.html").write_text(page, encoding="utf-8")
print("Published", len(sections), "comparisons and", len(videos), "videos")
