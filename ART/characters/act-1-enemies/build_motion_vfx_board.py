"""Доска выбора: рефы движений Higgsfield и целевые кадры эффектов новых мобов леса (26.09).

python build_motion_vfx_board.py → review/new-mobs-motion-vfx-2026-09-26/index.html
"""

import shutil
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
OUT = HERE / "review" / "new-mobs-motion-vfx-2026-09-26"

MOBS = [
    {
        "key": "shipomet", "folder": "5.forest-elite-shipomet", "name": "Шипомет",
        "refs": [
            ("idle_walk", "Покой → ходьба", "Покачивается, потом идёт длинными ходульными шагами."),
            ("line_cast", "Линия шипов", "Руки-шипы вверх, на ≈1,4 с вонзает в землю, держит ≈1,5 с, выпрямляется. В игре замах сожму до 0,9 с."),
            ("burst", "Взрыв вплотную", "Сжимается и распахивается звездой. Шипы, что вырастают в рефе, делаю эффектом, не скелетом."),
            ("death", "Смерть", "Шатается, падает на колени, валится кучей лоз."),
        ],
        "frames": [
            ("1-line-spikes.png", "Линия: одиночные шипы", "Четыре шипа по очереди, ближний выше всех, последний только трескает землю."),
            ("2-line-root-clusters.png", "Линия: пучки корней", "Пучки из 3–4 изогнутых шипов, трещина бежит дальше."),
            ("3-burst.png", "Взрыв вплотную", "Круг шипов во все стороны, пыль и щепки по краю."),
        ],
    },
    {
        "key": "snarer", "folder": "7.forest-root-snarer", "name": "Корнехват",
        "refs": [
            ("idle_walk", "Покой → ходьба", "Тяжёлая «горилья» походка на лапах-плитах."),
            ("slam", "Удар лапами в землю", "Встаёт на дыбы, бьёт лапами в землю и держит их там. В игре подъём сожму до 0,5 с, выдёргивание доделаю сам."),
            ("death", "Смерть", "Оседает и падает на брюхо, грибы качаются."),
        ],
        "frames": [
            ("1-roots-snare.png", "Корни оплетают", "Под героем трескается круг, корни обвивают ноги, от лап бегут трещины."),
            ("2-root-cage.png", "Клетка из корней", "Кольцо острых корней смыкается над героем."),
        ],
    },
    {
        "key": "splitter", "folder": "8.forest-splitbark", "name": "Расщепень",
        "refs": [
            ("idle_walk", "Покой → ходьба", "Быстрая рысь, створки покачиваются. Уходит из кадра к концу — для шага хватает."),
            ("bite", "Укус", "Приседает, створки приоткрываются, выпад клювом, отход."),
            ("death", "Смерть", "Створки отваливаются, внутри остаётся «голый» зверь. С решением «две копии в панцире» спорит — смерть большого сделаю коротким треском, а дети выскакивают в том же кадре."),
        ],
        "frames": [
            ("1-burst-leap.png", "Раскол: дети в прыжке", "Пыль, щепки, грибные шляпки, две копии на 60% прыгают в стороны."),
            ("2-crater-split.png", "Раскол: воронка", "Треснувший панцирь в воронке, дети уже стоят по бокам."),
        ],
    },
]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    sections = []
    for mob in MOBS:
        folder = HERE / mob["folder"] / "production"
        target = OUT / mob["key"]
        target.mkdir(exist_ok=True)
        ref_cards = []
        for slug, title, note in mob["refs"]:
            shutil.copy2(folder / "references" / f"{slug}.mp4", target / f"{slug}.mp4")
            ref_cards.append(f"""
      <figure class="card"><video src="{mob['key']}/{slug}.mp4" autoplay loop muted playsinline controls></video>
        <figcaption><strong>{title}</strong><span>{note}</span></figcaption></figure>""")
        frame_cards = []
        for index, (name, title, note) in enumerate(mob["frames"], 1):
            image = Image.open(folder / "vfx_target_frames_2026-09-26" / name).convert("RGB")
            image.thumbnail((1600, 1600))
            jpg = name.replace(".png", ".jpg")
            image.save(target / jpg, quality=88)
            frame_cards.append(f"""
      <figure class="card frame"><a href="{mob['key']}/{jpg}" target="_blank"><img src="{mob['key']}/{jpg}" alt="{title}"></a>
        <figcaption><strong>{index}. {title}</strong><span>{note}</span></figcaption></figure>""")
        sections.append(f"""
  <section id="{mob['key']}">
    <h2>{mob['name']}</h2>
    <h3>Рефы движений</h3>
    <div class="grid refs">{''.join(ref_cards)}
    </div>
    <h3>Целевые кадры эффектов</h3>
    <div class="grid frames">{''.join(frame_cards)}
    </div>
  </section>""")

    html = f"""<!doctype html>
<html lang="ru">
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Новые мобы леса · движения и эффекты</title>
<style>
:root{{color-scheme:dark;--bg:#141b19;--panel:#202925;--line:#3b473e;--ink:#eae9df;--muted:#b3beb1;--accent:#cad69d}}
*{{box-sizing:border-box}}body{{margin:0;background:var(--bg);color:var(--ink);font:16px/1.55 "Segoe UI",sans-serif}}
main{{max-width:1400px;margin:auto;padding:36px 32px 70px}}h1{{font-size:38px;line-height:1.1;margin:8px 0 10px;font-weight:650}}
h2{{font-size:30px;margin:48px 0 4px;padding-top:22px;border-top:1px solid var(--line);font-weight:620}}h3{{font-size:15px;letter-spacing:.12em;text-transform:uppercase;color:var(--accent);margin:22px 0 10px;font-weight:600}}
p{{margin:6px 0;color:var(--muted);max-width:75ch}}.eyebrow{{font-size:12px;letter-spacing:.14em;text-transform:uppercase;color:var(--accent);margin:0}}
nav{{display:flex;gap:10px;flex-wrap:wrap;margin:16px 0}}nav a{{color:var(--ink);border:1px solid var(--line);border-radius:30px;padding:6px 14px;text-decoration:none}}nav a:hover,nav a:focus-visible{{border-color:var(--accent);outline:none}}
.grid{{display:grid;gap:16px}}.refs{{grid-template-columns:repeat(auto-fill,minmax(300px,1fr))}}.frames{{grid-template-columns:repeat(auto-fill,minmax(420px,1fr))}}
.card{{margin:0;border:1px solid var(--line);border-radius:12px;overflow:hidden;background:var(--panel)}}.card video,.card img{{display:block;width:100%;background:#1b2220}}.card video{{aspect-ratio:1}}
figcaption{{padding:12px 14px;display:flex;flex-direction:column;gap:4px}}figcaption span{{color:var(--muted);font-size:14px}}
@media (max-width:700px){{main{{padding:24px 16px 50px}}.frames{{grid-template-columns:1fr}}}}
</style>
<main>
  <p class="eyebrow">Лес · новые мобы · 26 сентября 2026</p>
  <h1>Движения и эффекты: Шипомет, Корнехват, Расщепень</h1>
  <p>Рефы движений — ролики Higgsfield по утверждённым моделям; по ним делаю клипы в Blender. Целевые кадры эффектов нарисованы поверх нашего кадра игры; по выбранному собираю эффект из паков CFXR/Hovl. Выбери по кадру на удар; рефы — годятся или переснять.</p>
  <nav>{"".join(f'<a href="#{m["key"]}">{m["name"]}</a>' for m in MOBS)}</nav>
  {"".join(sections)}
</main>
</html>
"""
    (OUT / "index.html").write_text(html, encoding="utf-8")
    print(OUT / "index.html")


if __name__ == "__main__":
    main()
