"""Доска приёмки моделей новых мобов леса (26.09): Шипомет, Корнехват, Расщепень.

Собирает review/new-mobs-models-2026-09-26/: копирует рендеры из <моб>/production/review/model_r01,
склеивает поворот в mp4 и пишет index.html. Запуск: python build_new_mobs_board.py
"""

import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = HERE / "review" / "new-mobs-models-2026-09-26"

MOBS = [
    {
        "key": "shipomet", "folder": "5.forest-elite-shipomet", "name": "Шипомет", "role": "Элита · линия шипов",
        "height": "2,7 м", "note": "Тонкий высокий силуэт, вместо кистей — два длинных шипа. Рост взят меньше Вендиго (3,1 м), чтобы не спорить с ним по силуэту.",
    },
    {
        "key": "snarer", "folder": "7.forest-root-snarer", "name": "Корнехват", "role": "Обычный · круг под игроком → корни",
        "height": "1,35 м", "note": "Приземистый грибной краб: тяжёлые лапы-плиты вонзает в землю, грибы на спине. Силуэт не похож ни на рой, ни на кабана, ни на гуманоидов.",
    },
    {
        "key": "splitter", "folder": "8.forest-splitbark", "name": "Расщепень", "role": "Обычный · после смерти — два детёныша",
        "height": "1,3 м, детёныш 0,78 м", "note": "Детёныш — та же модель на 60%, на сравнении стоит рядом с большим. Панцирь трескается, выскакивают две копии в панцире — как решено.",
    },
]
VIEWS = [("game_camera", "Игровая камера"), ("front", "Спереди"), ("left", "Слева"), ("back", "Сзади"), ("right", "Справа")]


def ffmpeg_exe():
    tools = ROOT / "artifacts" / "tools" / "python"
    sys.path.insert(0, str(tools))
    import imageio_ffmpeg  # noqa: E402
    return imageio_ffmpeg.get_ffmpeg_exe()


def jpeg(source, target, size):
    from PIL import Image
    image = Image.open(source).convert("RGB")
    image.thumbnail((size, size))
    image.save(target, quality=88)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    ffmpeg = ffmpeg_exe()
    cards = []
    for mob in MOBS:
        folder = HERE / mob["folder"]
        review = folder / "production" / "review" / "model_r01"
        target = OUT / mob["key"]
        target.mkdir(exist_ok=True)
        for stale in target.glob("*.png"):
            stale.unlink()
        jpeg(folder / "art.png", target / "concept.jpg", 1100)
        jpeg(folder / "production" / "references" / "model_input_r01.png", target / "model_input.jpg", 1100)
        for view, _ in VIEWS + [("vs_pelag", ""), ("vs_pelag_game", "")]:
            jpeg(review / f"{mob['key']}_{view}.png", target / f"{view}.jpg", 900)
        subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-framerate", "12", "-i", str(review / "turntable_frames" / "%02d.png"),
                        "-vf", "scale=640:-2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "+faststart",
                        str(target / "turntable.mp4")], check=True)
        task = json.loads(next((folder / "tripo-out").glob("*/task.json")).read_text(encoding="utf-8"))
        mob["task"] = task.get("task_id", "")
        cards.append(mob)

    def card(mob):
        key = mob["key"]
        views = "".join(
            f'<button type="button" data-view="{key}/{view}.jpg"{" aria-pressed=\"true\"" if i == 0 else ""}>{label}</button>'
            for i, (view, label) in enumerate(VIEWS))
        return f"""
<section class="mob" id="{key}">
  <header class="mob-head">
    <div><p class="eyebrow">{mob['role']}</p><h2>{mob['name']}</h2></div>
    <p class="size">рост в игре ≈ {mob['height']}</p>
  </header>
  <div class="mob-grid">
    <figure class="viewer"><img src="{key}/game_camera.jpg" alt="{mob['name']}: модель" data-main><figcaption class="toolbar">{views}</figcaption></figure>
    <div class="side">
      <figure><video src="{key}/turntable.mp4" autoplay loop muted playsinline></video><figcaption>Поворот</figcaption></figure>
      <div class="pair">
        <figure><img src="{key}/concept.jpg" alt="Концепт"><figcaption>Концепт</figcaption></figure>
        <figure><img src="{key}/model_input.jpg" alt="Вход для 3D"><figcaption>Вход для 3D</figcaption></figure>
      </div>
      <p class="note">{mob['note']}</p>
    </div>
  </div>
  <figure class="compare"><img src="{key}/vs_pelag_game.jpg" alt="Рядом с Пелагом, игровой угол"><figcaption>Рядом с Пелагом (1,8 м), угол как в игре</figcaption></figure>
</section>"""

    html = f"""<!doctype html>
<html lang="ru">
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Новые мобы леса · модели</title>
<style>
:root{{color-scheme:dark;--bg:#141b19;--panel:#202925;--line:#3b473e;--ink:#eae9df;--muted:#b3beb1;--accent:#cad69d}}
*{{box-sizing:border-box}}body{{margin:0;background:var(--bg);color:var(--ink);font:16px/1.55 "Segoe UI",sans-serif}}
main{{max-width:1380px;margin:auto;padding:38px 32px 70px}}h1{{font-size:40px;line-height:1.1;margin:8px 0 10px;font-weight:650}}
h2{{font-size:30px;margin:0;font-weight:620}}p{{margin:6px 0;color:var(--muted)}}.eyebrow{{font-size:12px;letter-spacing:.14em;text-transform:uppercase;color:var(--accent);margin:0}}
nav{{display:flex;gap:10px;flex-wrap:wrap;margin:18px 0 8px}}nav a{{color:var(--ink);border:1px solid var(--line);border-radius:30px;padding:6px 14px;text-decoration:none}}nav a:hover{{border-color:var(--accent)}}
.mob{{margin-top:48px;padding-top:26px;border-top:1px solid var(--line)}}.mob-head{{display:flex;justify-content:space-between;align-items:flex-end;gap:16px;margin-bottom:16px;flex-wrap:wrap}}.size{{font-variant-numeric:tabular-nums}}
.mob-grid{{display:grid;grid-template-columns:minmax(0,1.5fr) minmax(0,1fr);gap:22px}}
figure{{margin:0}}.viewer{{border:1px solid var(--line);border-radius:14px;overflow:hidden;background:var(--panel)}}.viewer img{{display:block;width:100%;aspect-ratio:1;object-fit:contain;background:#1b2220}}
.toolbar{{display:flex;gap:8px;flex-wrap:wrap;padding:12px}}button{{font:inherit;cursor:pointer;border:1px solid var(--line);border-radius:8px;padding:6px 12px;color:var(--ink);background:#29342d}}button:hover,button:focus-visible{{border-color:var(--accent);outline:none}}button[aria-pressed=true]{{background:var(--accent);color:#1c271d;border-color:var(--accent)}}
.side{{display:flex;flex-direction:column;gap:14px}}.side video,.side img{{display:block;width:100%;border-radius:12px;border:1px solid var(--line);background:#1b2220}}.side figcaption,.compare figcaption{{font-size:13px;color:var(--muted);padding-top:4px}}
.pair{{display:grid;grid-template-columns:1fr 1fr;gap:12px}}.pair img{{aspect-ratio:1;object-fit:contain;background:#fff}}
.note{{padding:12px 14px;border-left:3px solid #819567;background:#27312a;font-size:14px;color:#d3dccb}}
.compare{{margin-top:18px}}.compare img{{display:block;width:100%;border-radius:12px;border:1px solid var(--line)}}
@media (max-width:860px){{main{{padding:24px 16px 50px}}.mob-grid{{grid-template-columns:1fr}}}}
</style>
<main>
  <p class="eyebrow">Лес · новые мобы · 26 сентября 2026</p>
  <h1>Модели: Шипомет, Корнехват, Расщепень</h1>
  <p>Сырые модели Tripo по чистым видам с Higgsfield (по концептам). Это приёмка формы и силуэта; риг, анимации и эффекты — после утверждения. Рост в игре подгоняется при сборке.</p>
  <nav>{"".join(f'<a href="#{m["key"]}">{m["name"]}</a>' for m in cards)}</nav>
  {"".join(card(m) for m in cards)}
</main>
<script>
document.querySelectorAll('.mob').forEach(section => {{
  const main = section.querySelector('[data-main]');
  section.querySelectorAll('[data-view]').forEach(button => button.addEventListener('click', () => {{
    main.src = button.dataset.view;
    section.querySelectorAll('[data-view]').forEach(other => other.setAttribute('aria-pressed', other === button ? 'true' : 'false'));
  }}));
}});
</script>
</html>
"""
    (OUT / "index.html").write_text(html, encoding="utf-8")
    print(OUT / "index.html")


if __name__ == "__main__":
    main()
