from pathlib import Path
import json, re, shutil
root = Path(__file__).resolve().parent.parent.parent
page_path = root / 'review/index.html'
archive = root / 'review/nanobanana-rejected.html'
if not archive.exists():
    shutil.copy2(page_path, archive)
manifest_path = root / 'concepts/imagegen-revision/manifest.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
page = '''<!doctype html>
<html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Пелаг — новые варианты Imagegen</title>
<style>
:root{color-scheme:light}*{box-sizing:border-box}body{margin:0;background:#eee7da;color:#302c27;font:17px/1.5 system-ui,sans-serif}main{max-width:1160px;margin:auto;padding:30px 24px 55px}h1{font-size:30px;line-height:1.2}h2{font-size:22px;margin:0 0 8px}p{max-width:850px}article{margin:24px 0;padding:20px;background:#fff9ee;border:1px solid #d2c3ae;border-radius:12px}.concept{display:block;width:100%;border-radius:8px;cursor:zoom-in}.small{font-size:14px;color:#716354}.original{max-width:320px;width:100%;display:block}a{color:#954a4a}details{font-size:14px;margin-top:16px}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:13px;background:#eee5d5;padding:15px;border-radius:8px}
</style><main>
<p class="small">30 сентября 2026 · Встроенный Imagegen · Направление ждёт твоего выбора</p>
<h1>Пелаг — доработка нынешнего героя</h1>
<p>Два новых варианта по исходным изображениям Пелага, сабли, якоря и настоящей игры. Модель в Unity пока прежняя.</p>
'''
for item, title, summary in [
    ('A', 'A · Более боевой характер', 'Увереннее выражение лица и сильнее контраст крупных форм.'),
    ('B', 'B · Более собранный образ', 'Спокойнее выражение лица, компактнее волосы, проще крепление за спиной.')]:
    prompt = (root / 'concepts/imagegen-revision' / (item + '.prompt.txt')).read_text(encoding='utf-8')
    import html
    page += f'<article id="{item}"><h2>{title}</h2><p>{summary}</p><a href="../concepts/imagegen-revision/{item}.png" target="_blank"><img class="concept" src="../concepts/imagegen-revision/{item}.png" alt="Новый концепт {item}"></a><p class="small">Нажми для полного размера. Это концепт направления; посадку оружия и совместимость проверим отдельно в 3D.</p><details><summary>Точный запрос Imagegen</summary><pre>{html.escape(prompt)}</pre></details></article>'
page += '''<h2>Исходный Пелаг</h2><a href="../references/pelag_identity.jpg" target="_blank"><img class="original" src="../references/pelag_identity.jpg" alt="Исходный герой"></a>
<p class="small"><a href="../concepts/imagegen-revision/manifest.json">Исходники, параметры и запросы</a> · <a href="nanobanana-rejected.html">Предыдущие отклонённые варианты Nano Banana</a></p>
</main></html>'''
page_path.write_text(page, encoding='utf-8')
for name in ['requests.json', 'results.json']:
    path = root / 'concepts' / name
    data = json.loads(path.read_text(encoding='utf-8'))
    data['stage'] = 'rejected_by_owner'
    data['owner_review'] = 'Nano Banana quality rejected; new current candidates in imagegen-revision/manifest.json'
    if name == 'requests.json':
        data['tools']['higgsfield'] = 'Five references uploaded, two paid jobs completed; owner rejected both. No further paid jobs in Imagegen revision.'
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
state_path = root.parents[3] / 'AGENTS/STATE.md'
state = state_path.read_text(encoding='utf-8')
old = next(line for line in state.splitlines() if line.startswith('- **Два концепта Higgsfield готовы'))
new = '- **Предыдущие два концепта Nano Banana отклонены владельцем. Новые варианты A/B через встроенный Imagegen готовы, ждём выбора; новая 3D-модель не произведена/не принята.** Результаты и точные запросы — `ART/characters/pelag/demo-polish-2026-09-30/concepts/imagegen-revision/`; актуальное сравнение — `review/index.html`. Использованы оригинальные изображения Пелага, сабли, якоря и два игровых кадра, а не отклонённые генерации. По последнему указанию владельца выбран встроенный Imagegen, дополнительных заданий/кредитов Higgsfield в этой ревизии нет. Отклонённый пакет и его расход 4 кредита сохранены в предыдущих receipts и `review/nanobanana-rejected.html`. Текущие рисунки не заменяют спецификацию исходного оружия, креплений и скелета; до выбора направления 3D-правки не начинаем.'
state_path.write_text(state.replace(old, new, 1), encoding='utf-8')
log_path = root.parents[3] / 'AGENTS/LOG.md'
entry = '''
## 30 сентября 2026 — Пелаг: отказ от Nano Banana, два варианта через Imagegen

Владелец отклонил качество обоих концептов Nano Banana и прямо разрешил переделать через GPT Image в Higgsfield либо встроенный Imagegen. Выбран встроенный `image_gen.imagegen`: две отдельные генерации с оригинальными пятью изображениями героя/оружия/игры. Отрицательно принятые концепты не использованы как основа. Запросы уточняют единственную саблю в правой руке, пустую левую, функциональное близкое крепление большого исходного якоря, более взрослое лицо и крупные формы без смены пропорций/кроя/сандалий.

Оба результата просмотрены; дублирования сабли внутри одной позы нет. A имеет более активное выражение лица и сложнее ремень, B — компактнее волосы и проще крепление. Ни один не обозначен принятым. PNG 1536×1024 и точные запросы сохранены в `ART/characters/pelag/demo-polish-2026-09-30/concepts/imagegen-revision/`; manifest хранит ссылки на оригинальные входы, исходные файлы генератора и хэши копий. `review/index.html` показывает новые результаты; прежняя страница сохранена как `review/nanobanana-rejected.html`. Higgsfield в этой ревизии не вызывался для новых заданий и кредитов не расходовал. Unity/Blender-модель и работы соседних агентов не менялись; следующий шаг — выбор направления владельцем.
'''
heading = '## 30 сентября 2026 — Пелаг: отказ от Nano Banana, два варианта через Imagegen'
if heading not in log_path.read_text(encoding='utf-8'):
    with log_path.open('a', encoding='utf-8', newline='\n') as output:
        output.write(entry)
print('Current review and owner rejection recorded')

