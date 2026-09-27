"""Write review/animation_r01/index.html from manifest.json, validation.json and the unity export.json.

python review_page.py
"""
import html
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE.parent / "review" / "animation_r01"
man = json.loads((OUT / "manifest.json").read_text(encoding="utf-8"))
val = json.loads((HERE / "validation.json").read_text())["takes"]
exp = json.loads((HERE / "unity_package" / "export.json").read_text(encoding="utf-8"))
fbxc = json.loads((HERE / "unity_package" / "fbx_check.json").read_text())

TITLE = {"Idle": "Ожидание", "Walk": "Ходьба", "LineCast": "Линия шипов", "Burst": "Взрыв вплотную",
         "Shot": "Выстрел шипом (новая)", "Hit": "Попадание", "Death": "Смерть"}
SIM = {"LineCast": "кадр 24 = шипы в земле; сим: первый шип из земли на тике 27, волна до 45, затем 30 тиков наказания",
       "Burst": "кадр 21 = поза звезды; сим: замах 21 тик", "Shot": "кадр 21 = шип срывается с кончика; сим: замах 21 тик"}
REFNOTE = {"Idle": "idle_walk.mp4, 0–2 с", "Walk": "idle_walk.mp4, 2–4 с", "LineCast": "line_cast.mp4", "Burst": "burst.mp4",
           "Death": "death.mp4"}


def fmt(x, nd=3):
    return f"{x:.{nd}f}".replace(".", ",")


def checks(take):
    v = val[take]
    rows = [("Пересечение земли телом", f"{fmt(v['min_z_body'] * 100, 1)} см (мин. z, без шипов рук)"),
            ("Длины костей", f"≤ {v['bone_length_err_max']:.1e}"),
            ("Корень", "объект арматуры не двигается; таз в плоскости XY ≤ " + fmt(v["hips_xy_max"] * 100, 1) + " см")]
    if take in ("Idle", "Walk"):
        rows.append(("Шов петли", f"поза 0 = поза {v['frames'][1]} (расхождение {fmt(v['loop_seam_vertex_m'] * 1000, 2)} мм)"))
    if take == "Walk":
        rows.append(("Проскальзывание опорной ноги", f"≤ {fmt(v['planted_claw_slip_per_stance_m_est'] * 100, 1)} см за опору (цель < 2 см)"))
        rows.append(("Скорость / шаг", "2,2 м/с · 1,76 м за цикл 24 кадра · шаг 0,88 м"))
    if take == "LineCast":
        rows.append(("Первое касание земли", f"кадр {v['spike_tip_first_ground_frame']} (кончики шипов, z ≤ 5 мм)"))
        z = v["spike_tip_z_22_26"]
        rows.append(("Высота кончиков 22–26", " · ".join(f"{k}: {fmt(max(a), 2)} м" for k, a in z.items())))
        rows.append(("Шипы в земле 24–60", f"кончики не выше {fmt(v['spike_tips_buried_24_60_max_z'] * 100, 1)} см, дрейф {fmt(v['spike_tip_drift_hold_30_60_m'] * 1000, 1)} мм"))
    if take == "Burst":
        s = v["tip_spread_m"]
        rows.append(("Размах шипов", f"кадр 18: {fmt(s['f18'], 2)} м → кадр 21: {fmt(s['f21'], 2)} м (макс. {fmt(s['max'], 2)} м; с кадра 21 держится до 27)"))
    if take == "Shot":
        m = v["muzzle"]
        rows.append(("Дуло", f"кончик кости RightHand, локально (0; {fmt(m['bone_local_offset_blender'][1], 3)}; 0) м по оси кости"))
        rows.append(("Дуло в кадре 21", f"Unity (x, y, z) = ({', '.join(fmt(x, 2) for x in m['world_unity_at_release'])}) от корня; направление ({', '.join(fmt(x, 2) for x in m['spike_dir_unity'])})"))
        rows.append(("Скорость кончика перед выстрелом", f"{fmt(m['tip_speed_into_release_mps'], 1)} м/с"))
    if take == "Death":
        rows.append(("Финальная поза", f"мин. z {fmt(v['final_min_z'] * 100, 1)} см, макс. z {fmt(v['final_max_z'], 2)} м, неподвижна 44–48 ({fmt(v['still_44_48_max_move_m'] * 1000, 1)} мм)"))
    return "".join(f"<tr><td>{html.escape(a)}</td><td>{html.escape(b)}</td></tr>" for a, b in rows)


cards = []
for take, t in man["takes"].items():
    n = t["frames"][1]
    c = t["contact"]
    tag = f"<span class='pill hot'>контакт {c}</span>" if c is not None else ""
    tag += "<span class='pill'>петля</span>" if t["loop"] else ""
    ref = (f"<figure class='ref'><img src='{t['reference_sheet']}' alt='Лист референса {take}' loading='lazy'>"
           f"<figcaption>Референс: {REFNOTE[take]} (6 кадров/с)</figcaption></figure>") if t["reference_sheet"] else \
        "<div class='noref'>Референса нет: клип поставлен с нуля по описанию.</div>"
    stills = "".join(
        f"<figure class='{'hot' if f == c else ''}'><img src='stills/{take}_game_{f:04d}.jpg' alt='{take} кадр {f}' loading='lazy'>"
        f"<figcaption>{f}</figcaption></figure>" for f in t["stills"])
    phases = " → ".join(f"<b>{p['from']}</b> {html.escape(p['label'])}" for p in t["phases"])
    sim = f"<p class='sim'>{html.escape(SIM[take])}</p>" if take in SIM else ""
    cards.append(f"""
<section class="take" id="{take}">
  <header><h2>{TITLE[take]} <code>ForestThorncaster_{take}</code></h2><div>{tag}<span class="pill">0–{n} · {n / 30:.2f} с</span></div></header>
  <p class="phases">{phases}</p>{sim}
  <div class="grid">
    <figure><video src="{t['videos']['game']}" autoplay loop muted playsinline controls></video><figcaption>Игровая камера (52° вниз, 3/4)</figcaption></figure>
    <figure><video src="{t['videos']['side']}" autoplay loop muted playsinline controls></video><figcaption>Сбоку (слева от моба)</figcaption></figure>
    {ref}
  </div>
  <div class="stills">{stills}</div>
  <table>{checks(take)}</table>
</section>""")

issues = [
    "Позы поставлены процедурно по листам референса (не покадровый трекинг видео); ритм подогнан под тики симуляции.",
    "Взрыв: на референсе «звезда» — это много шипов, выросших из тела; риг двигает только две руки-шипа, остальное должен дать VFX в кадре 21.",
    "Ходьба: быстрый вынос ноги на ходулях — у отрыва и постановки стопы пик ускорения голени/стопы ~0,1 м/кадр² (так задуман широкий шаг, не рывок шва петли).",
    "Линия полёта шипа и летящий шип на видео выстрела — оверлей ревью, в FBX их нет. Для игры есть пустышка Muzzle_RightSpike на кончике правого шипа.",
    "Кости head_end и headfront не анимируются (точки крепления). Мелкие шипы на конечностях слегка гнутся у локтей и колен (от рига).",
    "Колено при глубоком сгибе (ходьба 18, стойка линии 30–60, смерть 19–20) — кора на сгибе растягивается до ~13 см на одном ребре; сверху выглядит как скруглённое колено, разрывов нет.",
    "Выстрел: в прицеле 12–18 шип над головой смотрит вперёд, на игровую камеру, поэтому с неё он укорочен ракурсом.",
    "Меш несимметричный (ноги и руки разной длины), поэтому значения поз по сторонам разные; зеркалить клипы в Unity нельзя.",
    "Материал FBX ссылается только на Color и NormalGL; ORM лежит в Textures/ и подключается вручную.",
]
FIXED3 = [
    "Кисти (риг): у корня руки-шипа вес кисти граничил с весом предплечья через одно ребро — при сгибе запястья кора рвалась тонкой щепкой до 16 см (линия 8, взрыв 20, смерть 44). Вес кисти/предплечья теперь плавно переходит на 18 см вокруг запястья, сам шип остаётся жёстким; растяжение запястья ≤ 3,6 см.",
    "Выстрел 4–10: шип складывался назад в предплечье (сгиб 165°, шипа не было видно). Теперь рука уходит назад маятником и шип переворачивается над головой; сгиб ≤ 107° (это сам прицел).",
    "Линия шипов 20–24: удар замирал на кадре 22 в 0,75 м над землёй и дополз до земли (скорость кончика 68 → 13 → 19 м/с). Теперь разгон до контакта: 25 → 31 → 38 → 37 м/с, кадр 23 — 1,15 м над землёй, кадр 24 — в земле.",
    "Взрыв: шипы продолжали расходиться после выпуска (максимум на 23-м); теперь самая широкая звезда на кадре 21 и держится до 27. Кадр 20 больше не заламывает шипы на 100° назад.",
    "Смерть: тело висело в воздухе — кадры 16–21 без опоры до 8 см, «на коленях» 22–31 колени в 2–5 см над землёй. Теперь самая низкая точка тела на земле в каждом кадре. Убраны две остановки посреди падения (кадры 17 и 33–35).",
]
FIXED = [
    "Линия шипов 0–18: подъём рук переставлен — локти больше не перескакивают и рука не выпрямляется рывком (было в кадрах 4–10).",
    "Взрыв 0–8: шипы идут к груди вперёд-внутрь, без замаха назад и без рывка выпрямленной руки (было в кадре 5).",
    "Смерть: колени и бёдра больше не уходят в землю (было до 4,7 см в кадрах 33–38); лежит на земле без зазора, неподвижна 44–48.",
]
page = f"""<!doctype html>
<html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Шипомет · анимации r01</title>
<style>
:root{{color-scheme:dark;--bg:#141b19;--panel:#202925;--line:#3b473e;--ink:#eae9df;--muted:#b3beb1;--accent:#cad69d;--hot:#ff8c28}}
*{{box-sizing:border-box}}body{{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 'Segoe UI',sans-serif}}
main{{max-width:1500px;margin:auto;padding:28px 16px 60px}}h1{{font-size:34px;margin:6px 0 8px}}h2{{font-size:20px;margin:0}}
p{{color:var(--muted);margin:6px 0}}.eyebrow{{font-size:12px;letter-spacing:.12em;color:var(--accent)}}
code{{font-size:13px;color:var(--accent);margin-left:8px}}.pill{{display:inline-block;border:1px solid #53634f;border-radius:20px;padding:2px 10px;margin-left:6px;font-size:13px;color:var(--muted)}}
.pill.hot{{border-color:var(--hot);color:var(--hot)}}.take{{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:16px;margin:18px 0}}
.take header{{display:flex;justify-content:space-between;gap:10px;flex-wrap:wrap;align-items:center}}
.phases{{font-size:14px}}.phases b{{color:var(--ink)}}.sim{{color:var(--hot);font-size:14px}}
.grid{{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:12px;margin-top:10px}}
figure{{margin:0}}video,.grid img{{width:100%;display:block;border-radius:8px;background:#6d7773}}figcaption{{font-size:13px;color:var(--muted);padding:4px 2px}}
.ref img{{object-fit:contain}}.noref{{border:1px dashed var(--line);border-radius:8px;padding:16px;color:var(--muted);display:flex;align-items:center}}
.stills{{display:grid;grid-template-columns:repeat(auto-fill,minmax(120px,1fr));gap:8px;margin:12px 0}}.stills img{{width:100%;border-radius:6px;display:block}}
.stills figure.hot img{{outline:3px solid var(--hot)}}table{{border-collapse:collapse;width:100%;font-size:14px}}td{{border-top:1px solid var(--line);padding:6px 8px;vertical-align:top}}
td:first-child{{color:var(--muted);width:32%}}ul{{color:var(--muted)}}nav a{{color:var(--accent);margin-right:12px;text-decoration:none}}
</style></head><body><main>
<div class="eyebrow">THE WAR REMAINS · ЛЕС · ЭЛИТА · ШИПОМЕТ (ForestThorncaster)</div>
<h1>Анимации r01: 7 клипов на игровом риге</h1>
<p>Все клипы 30 кадров/с, на месте (корень не двигается, перемещает симуляция), модель 23 999 треугольников, 24 кости.
Кадры контакта совпадают с тиками симуляции. Ждёт проверки владельцем.</p>
<nav>{''.join(f'<a href="#{k}">{TITLE[k]}</a>' for k in man['takes'])}</nav>
{''.join(cards)}
<section class="take"><h2>Пакет для Unity</h2>
<p><code>animation/unity_package/ForestThorncaster.fbx</code> — меш, арматура, 7 дублей с точными именами, пустышка <code>Muzzle_RightSpike</code>; описание в <code>export.json</code>.
Проверка обратным импортом: {fbxc['bones']} костей, {fbxc['triangles']} треугольников, масштаб арматуры 1, анимации объекта нет, дуло в кадре 21 выстрела = ({', '.join(fmt(x, 2) for x in fbxc['shot_f21_muzzle_empty_world'])}) м в Blender.</p>
<h2 style="margin-top:14px">Исправлено после независимой проверки (третья сборка)</h2><ul>{''.join(f'<li>{html.escape(i)}</li>' for i in FIXED3)}</ul>
<p>Замеры проверки по реимпорту FBX: <code>animation/unity_package/verification.json</code>.</p>
<h2 style="margin-top:14px">Исправлено во второй сборке</h2><ul>{''.join(f'<li>{html.escape(i)}</li>' for i in FIXED)}</ul>
<h2 style="margin-top:14px">Известные проблемы</h2><ul>{''.join(f'<li>{html.escape(i)}</li>' for i in issues)}</ul></section>
</main></body></html>"""
(OUT / "index.html").write_text(page, encoding="utf-8")
print("PAGE_OK", OUT / "index.html")
