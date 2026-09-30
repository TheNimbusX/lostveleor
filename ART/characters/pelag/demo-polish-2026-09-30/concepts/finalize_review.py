import json, pathlib
root = pathlib.Path(__file__).resolve().parent.parent
manifest_path = root / 'concepts/requests.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
results = json.loads((root / 'concepts/results.json').read_text(encoding='utf-8'))
manifest['stage'] = 'concept_review'
manifest['owner_approval_required'] = 'Choose concept direction before any 3D candidate edits'
manifest['generation_jobs_submitted'] = 2
manifest['cost_observed_balance_delta'] = 4
manifest['owner_selection'] = None
for request, result in zip(manifest['requests'], results['results']):
    assert request['id'] == result['id']
    request['status'] = 'completed_awaiting_owner_review'
    request['job_id'] = result['job_id']
    request['result_file'] = result['file']
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
page_path = root / 'review/index.html'
page = page_path.read_text(encoding='utf-8')
page = page.replace('<title>Пелаг — пакет к запуску</title>', '<title>Пелаг — два концепта для выбора</title>')
page = page.replace('Генерации ещё не запускались', 'Два концепта готовы · Направление ещё не выбрано')
page = page.replace('<div class="cards"><article class="card">', '<div class="cards concepts"><article id="A" class="card">', 1)
page = page.replace('</article><article class="card"><h3>B', '</article><article id="B" class="card"><h3>B', 1)
old = '<p class="caption">Один лист: полный рост спереди и сзади, лицо крупно, игровой ракурс сверху. Это описание будущей генерации, не готовый концепт.</p>'
for id, note in [
    ('A', 'Ближе к исходному образу. Ошибка генерации: добавлена вторая сабля. Это не предложение менять вооружение.'),
    ('B', 'Взрослее лицо, собраннее причёска, крупнее цветовые блоки. Изменения кроя и крепления оружия требуют отдельного согласования.')]:
    markup = f'<a href="../concepts/{id}.png" target="_blank"><img class="concept-image" src="../concepts/{id}.png" alt="Концепт {id} Пелага"></a><p class="caption">{note} Нажми на изображение для полного размера.</p>'
    page = page.replace(old, markup, 1)
page = page.replace('<p><strong>Higgsfield · Nano Banana Pro · 2K · 16:9 · 2 запроса по 2 кредита = 4 кредита.</strong><br>Без платных повторов. После выбора концепта — доработка одной копии модели и отдельная приёмка 3D.</p>',
    '<p><strong>Две генерации выполнены. Расход — 4 кредита.</strong> Повторов не было. Оба результата использовали все пять референсов.</p><p>Выбираем направление лица, причёски и крупных форм костюма. Саблю и якорь сохраняем из настоящих исходников, ошибки рисунков не переносим в 3D. Модель в игре пока не менялась.</p><p><a href="../concepts/completion-receipt.json">Результаты, запросы и подтверждение референсов</a></p>')
page = page.replace('@media(max-width:700px)', '.concepts{grid-template-columns:1fr}.concept-image{width:100%;display:block;border-radius:8px;cursor:zoom-in}\n@media(max-width:700px)')
page_path.write_text(page, encoding='utf-8')
print('Review updated: two completed concepts, owner choice pending')

