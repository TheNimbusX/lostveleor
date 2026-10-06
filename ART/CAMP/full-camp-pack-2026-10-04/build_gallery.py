#!/usr/bin/env python3
"""Build a standalone local gallery from manifest.json; leave PNGs untouched."""

import json
import struct
from pathlib import Path
from urllib.parse import quote


GROUPS = {
    "00-existing/merchant-variants": "Готовые · лавки",
    "00-existing/goods-sheets": "Готовые · товары",
    "00-existing/lighting": "Готовые · свет",
    "00-existing": "Готовые объекты",
    "01-main": "Главные объекты",
    "02-merchant": "Торговая зона",
    "03-pelag": "Пелаг",
    "04-eni": "Эни · кузница",
    "05-ven": "Вен · лавка",
    "06-leo": "Лео · алхимия",
    "07-trees": "Деревья",
    "08-stones": "Камни",
    "09-woodland": "Лесные детали",
    "10-plants": "Растения",
    "11-ground-decals": "Земля · декали",
    "12-map-texture": "Карта · текстура",
}


def display_group(asset, relative_file):
    group = str(asset.get("group", "")).replace("\\", "/").strip("/")
    parts = relative_file.split("/")
    if parts[0] == "00-existing" and len(parts) > 2:
        subgroup = "/".join(parts[:2])
        if subgroup in GROUPS:
            return subgroup
    return group or parts[0]


def gallery_data(base, manifest):
    source_assets = manifest.get("assets")
    if not isinstance(source_assets, list):
        raise ValueError("manifest.json must contain an assets array")
    assets = []
    paths = set()
    for source in source_assets:
        if not isinstance(source, dict):
            raise ValueError("Each asset must be an object")
        relative_file = str(source.get("file", "")).replace("\\", "/")
        path = Path(relative_file)
        if path.suffix.lower() != ".png":
            continue
        resolved = (base / path).resolve()
        if path.is_absolute() or not resolved.is_relative_to(base):
            raise ValueError("Asset file must stay inside the pack: " + relative_file)
        if relative_file in paths:
            raise ValueError("Duplicate PNG in manifest: " + relative_file)
        paths.add(relative_file)
        with resolved.open("rb") as handle:
            header = handle.read(24)
        if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
            raise ValueError("Invalid PNG header: " + relative_file)
        width, height = struct.unpack(">II", header[16:24])
        if width <= 0 or height <= 0:
            raise ValueError("Invalid PNG dimensions: " + relative_file)
        group = display_group(source, relative_file)
        assets.append({
            "id": str(source.get("id", path.stem)),
            "title": str(source.get("title") or source.get("name_ru") or path.stem),
            "group": group,
            "group_title": GROUPS.get(group, group),
            "file": relative_file,
            "url": quote(relative_file, safe="/"),
            "dimensions": [width, height],
            "model_dimensions": str(source.get("model_dimensions") or source.get("dimensions") or ""),
            "kind": str(source.get("kind", "")),
            "notes": str(source.get("notes") or ""),
        })
    if not assets:
        raise ValueError("manifest.json contains no PNG assets")
    return assets


HTML = r'''<!doctype html>
<html lang="ru" data-theme="light">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Лагерь · полный пак объектов</title>
<style>
:root{color-scheme:light;--page:#f4f1e9;--panel:#fffdf8;--preview:#e2dfd6;--text:#302e2a;--muted:#706b61;--line:#d5cfc2;--accent:#786541;--active:#6e5c3e;--active-text:#fffaf0}
:root[data-theme="dark"]{color-scheme:dark;--page:#222323;--panel:#2b2d2d;--preview:#343737;--text:#eee9dd;--muted:#b7b1a5;--line:#484a47;--accent:#d8c394;--active:#d8c394;--active-text:#26241e}
*{box-sizing:border-box}body{margin:0;background:var(--page);color:var(--text);font:16px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}
main{max-width:1560px;margin:auto;padding:30px 24px 42px}header{display:flex;align-items:flex-start;justify-content:space-between;gap:24px;flex-wrap:wrap}h1{font-size:clamp(25px,3vw,36px);line-height:1.2;margin:0 0 9px;font-weight:650}p{margin:0;color:var(--muted)}
.themes{display:flex;gap:8px;align-items:center}.themes span{font-size:14px;color:var(--muted);margin-right:3px}button,input{font:inherit}button{border:1px solid var(--line);background:var(--panel);color:var(--text);border-radius:8px;cursor:pointer;padding:8px 13px}button:hover{border-color:var(--accent)}button[aria-pressed="true"]{background:var(--active);color:var(--active-text);border-color:var(--active)}button:focus-visible,a:focus-visible,input:focus-visible{outline:3px solid var(--accent);outline-offset:3px}
.controls{margin:25px 0 18px;padding:17px;background:var(--panel);border:1px solid var(--line);border-radius:12px}.groups{display:flex;gap:8px;flex-wrap:wrap}.group-count{opacity:.7;margin-left:6px;font-size:13px}.search-row{display:flex;align-items:center;justify-content:space-between;gap:16px;margin-top:15px;flex-wrap:wrap}.search{width:min(440px,100%);min-width:220px;background:var(--page);color:var(--text);border:1px solid var(--line);border-radius:8px;padding:9px 12px}#count{color:var(--muted);font-size:14px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:18px}.card{min-width:0;overflow:hidden;border:1px solid var(--line);border-radius:12px;background:var(--panel)}.preview{display:block;aspect-ratio:1;background:var(--preview);padding:14px;text-decoration:none}.preview img{display:block;width:100%;height:100%;object-fit:contain}.caption{padding:15px 16px 17px}.caption h2{font-size:17px;font-weight:600;line-height:1.35;margin:0 0 6px}.meta{font-size:13px;color:var(--muted)}.file{margin-top:7px;overflow-wrap:anywhere;font-size:12px;color:var(--muted)}.empty{padding:50px 12px;text-align:center;color:var(--muted)}footer{margin-top:24px;color:var(--muted);font-size:13px}
.kind,.model-dimensions,.notes{margin-top:6px;font-size:13px;line-height:1.4}.kind{color:var(--muted)}.notes{color:var(--text)}
@media(max-width:600px){main{padding:22px 14px}.controls{padding:12px}.grid{gap:13px;grid-template-columns:repeat(auto-fill,minmax(210px,1fr))}.themes{flex-wrap:wrap}button{padding:7px 10px;font-size:14px}}
</style>
</head>
<body>
<main>
<header>
<div><h1>Лагерь · полный пак объектов</h1><p id="intro">Отдельные PNG. Нажмите на изображение, чтобы открыть его целиком.</p></div>
<div class="themes" aria-label="Фон просмотра"><span>Фон</span><button type="button" data-theme-choice="light" aria-pressed="true">Светлый</button><button type="button" data-theme-choice="dark" aria-pressed="false">Тёмный</button></div>
</header>
<section class="controls" aria-label="Фильтры каталога">
<nav class="groups" id="groups" aria-label="Группы объектов"></nav>
<div class="search-row"><input class="search" id="search" type="search" placeholder="Найти по названию…" aria-label="Поиск объектов"><span id="count" role="status" aria-live="polite"></span></div>
</section>
<section class="grid" id="grid" aria-label="Объекты лагеря"></section>
<p class="empty" id="empty" hidden>По этому запросу ничего не найдено.</p>
<footer>Размер указан для исходного PNG. Фон просмотра помогает увидеть прозрачные края и не меняет изображения.</footer>
</main>
<script id="gallery-data" type="application/json">__GALLERY_DATA__</script>
<script>
"use strict";
const assets = JSON.parse(document.getElementById("gallery-data").textContent);
const grid = document.getElementById("grid");
const groups = document.getElementById("groups");
const search = document.getElementById("search");
const knownOrder = __GROUP_ORDER__;
let selectedGroup = "all";
let searchTerm = "";
const groupTitles = new Map();
const groupCounts = new Map();
for (const asset of assets) {
  groupTitles.set(asset.group, asset.group_title);
  groupCounts.set(asset.group, (groupCounts.get(asset.group) || 0) + 1);
}
const orderedGroups = Array.from(groupCounts.keys()).sort((a,b) => {
  const ai = knownOrder.indexOf(a), bi = knownOrder.indexOf(b);
  if (ai !== -1 || bi !== -1) return (ai === -1 ? 999 : ai) - (bi === -1 ? 999 : bi);
  return a.localeCompare(b, "ru");
});
function addGroupButton(key,title,count) {
  const button = document.createElement("button");
  button.type = "button";
  button.dataset.group = key;
  button.setAttribute("aria-pressed", String(key === selectedGroup));
  button.append(document.createTextNode(title));
  const total = document.createElement("span");
  total.className = "group-count";
  total.textContent = String(count);
  button.append(total);
  button.addEventListener("click", () => {
    selectedGroup = key;
    for (const control of groups.querySelectorAll("button")) control.setAttribute("aria-pressed", String(control.dataset.group === key));
    render();
  });
  groups.append(button);
}
addGroupButton("all", "Все объекты", assets.length);
for (const group of orderedGroups) addGroupButton(group, groupTitles.get(group), groupCounts.get(group));
function render() {
  const visible = assets.filter(asset => (selectedGroup === "all" || asset.group === selectedGroup) &&
    (!searchTerm || (asset.title + " " + asset.id + " " + asset.group_title).toLocaleLowerCase("ru").includes(searchTerm)));
  const fragment = document.createDocumentFragment();
  for (const asset of visible) {
    const card = document.createElement("article"); card.className = "card";
    const link = document.createElement("a"); link.className = "preview";
    link.href = asset.url; link.target = "_blank"; link.rel = "noopener";
    link.setAttribute("aria-label", "Открыть PNG: " + asset.title);
    const image = document.createElement("img"); image.src = asset.url; image.alt = asset.title;
    image.loading = "lazy"; image.decoding = "async";
    image.width = asset.dimensions[0]; image.height = asset.dimensions[1]; link.append(image);
    const caption = document.createElement("div"); caption.className = "caption";
    const heading = document.createElement("h2"); heading.textContent = asset.title;
    const meta = document.createElement("div"); meta.className = "meta";
    meta.textContent = asset.group_title + " · " + asset.dimensions[0] + " × " + asset.dimensions[1] + " px · PNG";
    const file = document.createElement("div"); file.className = "file"; file.textContent = asset.file;
    caption.append(heading,meta);
    if (asset.kind) {
      const kind = document.createElement("div"); kind.className = "kind"; kind.textContent = asset.kind; caption.append(kind);
    }
    if (asset.model_dimensions) {
      const dimensions = document.createElement("div"); dimensions.className = "model-dimensions";
      dimensions.textContent = "Ориентир размера: " + asset.model_dimensions; caption.append(dimensions);
    }
    if (asset.notes) {
      const notes = document.createElement("div"); notes.className = "notes"; notes.textContent = asset.notes; caption.append(notes);
    }
    caption.append(file); card.append(link,caption); fragment.append(card);
  }
  grid.replaceChildren(fragment);
  document.getElementById("count").textContent = "Показано " + visible.length + " из " + assets.length + " PNG";
  document.getElementById("empty").hidden = visible.length !== 0;
}
search.addEventListener("input", () => { searchTerm = search.value.trim().toLocaleLowerCase("ru"); render(); });
for (const button of document.querySelectorAll("[data-theme-choice]")) {
  button.addEventListener("click", () => {
    const theme = button.dataset.themeChoice;
    document.documentElement.dataset.theme = theme;
    for (const control of document.querySelectorAll("[data-theme-choice]")) control.setAttribute("aria-pressed", String(control.dataset.themeChoice === theme));
  });
}
document.getElementById("intro").textContent = assets.length + " отдельных PNG. Нажмите на изображение, чтобы открыть его целиком.";
render();
</script>
</body>
</html>
'''


def main():
    base = Path(__file__).resolve().parent
    manifest = json.loads((base / "manifest.json").read_text(encoding="utf-8-sig"))
    assets = gallery_data(base, manifest)
    # Escapes also protect local HTML if a title contains a closing script tag.
    data = json.dumps(assets, ensure_ascii=False).replace("&", "\\u0026").replace("<", "\\u003c").replace(">", "\\u003e")
    data = data.replace("\u2028", "\\u2028").replace("\u2029", "\\u2029")
    result = HTML.replace("__GALLERY_DATA__", data).replace("__GROUP_ORDER__", json.dumps(list(GROUPS)))
    (base / "gallery.html").write_text(result, encoding="utf-8", newline="\n")
    print("Created gallery.html: " + str(len(assets)) + " PNG")


if __name__ == "__main__":
    main()
