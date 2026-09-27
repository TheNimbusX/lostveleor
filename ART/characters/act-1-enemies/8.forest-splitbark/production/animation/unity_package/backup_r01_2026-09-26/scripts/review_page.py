"""Build ../review/animation_r01/index.html + manifest.json (run after review_media.py).

python review_page.py
"""
import html
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import review_media as rm  # noqa: E402
import review_text as tx  # noqa: E402
from takes import ORDER, TAKES  # noqa: E402

OUT = rm.OUT
PKG = HERE / "unity_package"
val = json.loads((HERE / "validation.json").read_text(encoding="utf-8"))
ver = json.loads((PKG / "verify.json").read_text(encoding="utf-8"))
exp = json.loads((PKG / "export.json").read_text(encoding="utf-8"))
e = html.escape

CSS = """
:root{color-scheme:dark;--bg:#141b19;--panel:#202925;--line:#3b473e;--ink:#eae9df;--muted:#b3beb1;--accent:#cad69d;--red:#e24034}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.55 'Segoe UI',sans-serif}
main{max-width:1500px;margin:auto;padding:30px 24px 65px}h1{font-size:36px;line-height:1.2;margin:8px 0 10px}
h2{font-size:22px;margin:0}p{margin:8px 0;color:var(--muted)}.eyebrow{font-size:12px;letter-spacing:.12em;color:var(--accent)}
.top{display:flex;gap:20px;align-items:flex-start;justify-content:space-between;margin-bottom:18px}
.badge{border:1px solid #647353;border-radius:30px;color:var(--accent);padding:8px 16px;white-space:nowrap}
.bar{position:sticky;top:0;z-index:2;display:flex;gap:10px;align-items:center;flex-wrap:wrap;padding:12px 16px;margin:0 0 22px;
background:var(--panel);border:1px solid var(--line);border-radius:12px}
button,select{font:inherit;padding:7px 12px;background:#29342d;border:1px solid #53634f;border-radius:7px;color:var(--ink);cursor:pointer}
button:hover{border-color:var(--accent)}button.mark{border-color:var(--red);color:#ffd9d4}
section.take{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:18px;margin:0 0 26px}
.head{display:flex;gap:14px;align-items:baseline;justify-content:space-between;flex-wrap:wrap}
.facts{font-size:14px;color:var(--accent);font-variant-numeric:tabular-nums}
.grid{display:grid;grid-template-columns:1fr 1fr 1.2fr;gap:14px;margin-top:14px}
figure{margin:0;background:#1a221f;border:1px solid var(--line);border-radius:10px;overflow:hidden}
figcaption{font-size:13px;color:var(--muted);padding:7px 10px}video,figure img{display:block;width:100%;height:auto;background:#8a958e}
.strip{margin-top:14px;overflow-x:auto;border-radius:10px;border:1px solid var(--line)}.strip img{display:block;max-width:none;height:230px}
.row{display:flex;gap:10px;flex-wrap:wrap;margin-top:12px}a{color:var(--accent)}
table{border-collapse:collapse;width:100%;font-size:14px;font-variant-numeric:tabular-nums}td,th{border-bottom:1px solid var(--line);padding:6px 8px;text-align:left}
th{color:var(--accent);font-weight:600}.note{border-left:3px solid #83956c;padding:10px 16px;background:#273128;margin:14px 0}
.note li{color:#d3ddcb;margin:6px 0}.ok{color:#9fd08a}.bad{color:var(--red)}
@media(max-width:900px){main{padding:18px 12px}.grid{grid-template-columns:1fr}h1{font-size:28px}.top{flex-direction:column}}
"""

JS = """
const vids=[...document.querySelectorAll('video')];
document.getElementById('speed').onchange=ev=>vids.forEach(v=>v.playbackRate=+ev.target.value);
document.getElementById('pauseAll').onclick=()=>vids.forEach(v=>v.pause());
document.getElementById('playAll').onclick=()=>vids.forEach(v=>v.play());
document.querySelectorAll('button[data-frame]').forEach(b=>b.onclick=()=>{
  const sec=b.closest('section');const f=+b.dataset.frame;
  sec.querySelectorAll('video').forEach(v=>{v.pause();v.currentTime=(f+0.5)/30;});});
document.querySelectorAll('button[data-play]').forEach(b=>b.onclick=()=>{
  b.closest('section').querySelectorAll('video').forEach(v=>{v.playbackRate=+document.getElementById('speed').value;v.play();});});
document.querySelectorAll('button[data-step]').forEach(b=>b.onclick=()=>{
  b.closest('section').querySelectorAll('video').forEach(v=>{v.pause();
  const f=Math.max(0,Math.round(v.currentTime*30-0.5)+(+b.dataset.step));v.currentTime=(f+0.5)/30;});});
"""


def video(src, cap):
    return (f'<figure><video src="{src}" muted loop autoplay playsinline preload="auto"></video>'
            f'<figcaption>{e(cap)}</figcaption></figure>')


def take_section(take):
    sp = TAKES[take]
    name, desc = tx.TAKES[take]
    n = sp["frames"]
    v = val["takes"][take]
    facts = [f"{n} кадров · {n / 30:.2f} с", "петля" if sp["loop"] else "один раз"]
    marks = rm.marks(take)
    for f, lab in sorted(marks.items()):
        facts.append(f"{lab.split(' · ')[0].lower()}: кадр {f}")
    buttons = "".join(f'<button class="mark" data-frame="{f}">к кадру {f}: {e(lab.split(" · ")[0].lower())}</button>'
                      for f, lab in sorted(marks.items()))
    ref = sp["ref"]
    return f"""
<section class="take" id="{take}"><div class="head"><h2>{e(name)} <span class="facts">· {take}</span></h2>
<span class="facts">{e(' · '.join(facts))}</span></div><p>{e(desc)}</p>
<div class="grid">{video(take + '_game.mp4', 'Игровая камера: 52° сверху, 3/4 спереди')}
{video(take + '_side.mp4', 'Сбоку (орто), морда влево')}
<figure><a href="../../references/{ref}.mp4"><img src="refs/{ref}_sheet.jpg" alt="{e(tx.REF_LABEL[ref])}"></a>
<figcaption>{e(tx.REF_LABEL[ref])} · 6 кадров/с, клик — видео</figcaption></figure></div>
<div class="row"><button data-play="1">играть</button><button data-step="-1">− кадр</button><button data-step="1">+ кадр</button>{buttons}</div>
<div class="strip"><img src="{take}_keys.jpg" alt="ключевые кадры {take}"></div>
<p class="facts">проверка: {'<span class="ok">пройдена</span>' if v['pass'] else '<span class="bad">не пройдена</span>'} ·
корень не двигается · кожа ниже земли max {abs(v['skin_min_z_m']) * 1000:.0f} мм ·
FBX совпадает с бейком: {ver['takes'][take]['max_vertex_err_m'] * 1000:.1f} мм</p></section>"""


def numbers():
    rows = []
    for take in ORDER:
        x = exp["takes"][take]
        rows.append(f"<tr><td>{take}</td><td>0–{x['frames'][1]}</td><td>{'да' if x['loop'] else 'нет'}</td>"
                    f"<td>{x.get('contact_frame', x.get('release_frame', '—'))}</td>"
                    f"<td>{'ok' if ver['takes'][take]['pass'] else 'FAIL'}</td></tr>")
    w = val["takes"]["ForestSplitter_Walk"]
    bt = exp["takes"]["ForestSplitter_Bite"]
    b = bt.get("beak_mesh_tip_at_contact_m", bt["bite_point_at_contact_m"])
    chk = PKG / "verification.json"
    check = ""
    if chk.exists():
        v3 = json.loads(chk.read_text(encoding="utf-8"))
        ok = sum(1 for c in v3["checks"].values() if c["pass"])
        state = ('<span class="ok">всё в порядке</span>' if v3["all_pass"]
                 else '<span class="bad">есть замечания</span>')
        wk = v3["checks"]["3_walk"]
        check = ('<p class="facts">Независимая проверка FBX (шаг 3, <code>unity_package/verification.json</code>): '
                 f'{ok} из {len(v3["checks"])} пунктов пройдены · {state} · рысь по подошвам '
                 f'{wk["measured_mps"][0]}–{wk["measured_mps"][1]} м/с, проскальзывание ≤ {wk["max_slide_mm"]:.0f} мм.</p>')
    return f"""
<section class="take"><h2>Пакет для Unity</h2>
<p><code>production/animation/unity_package/ForestSplitter.fbx</code> — меш {exp['triangles']} треугольников, {exp['deform_bones']}
костей, 6 дублей с точными именами, 30 кадров/с, оси как у Камнекопыта/Вендиго, пустышка FacingGuide в 1 м перед мордой.
Разметка — <code>export.json</code>.</p>
<table><tr><th>дубль</th><th>кадры</th><th>петля</th><th>метка</th><th>FBX</th></tr>{''.join(rows)}</table>
<p class="facts">Рысь: стоящие лапы {w['stance_foot_speed_mps'][0]}–{w['stance_foot_speed_mps'][1]} м/с при сим 3,1 м/с ·
кончик клюва на кадре 18: {b['forward']} м вперёд, {b['up']} м вверх.</p>{check}</section>"""


def page():
    seq_t, seq_d = tx.SEQUENCE
    issues = "".join(f"<li>{e(i)}</li>" for i in tx.ISSUES)
    body = "".join(take_section(t) for t in ORDER)
    return f"""<!doctype html><html lang="ru"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>{e(tx.TITLE)}</title><style>{CSS}</style></head>
<body><main><div class="top"><div><div class="eyebrow">THE WAR REMAINS · РАСЩЕПЕНЬ · ИГРОВЫЕ КЛИПЫ · R01</div>
<h1>{e(tx.TITLE)}</h1><p>{e(tx.LEAD)}</p></div><span class="badge">На просмотр владельцу</span></div>
<div class="bar"><button id="playAll">играть все</button><button id="pauseAll">пауза</button>
<select id="speed"><option value="1">скорость ×1</option><option value="0.5">×0,5</option><option value="0.25">×0,25</option></select>
{''.join(f'<a href="#{t}">{e(tx.TAKES[t][0])}</a>' for t in ORDER)}<a href="#sequence">смерть → дети</a><a href="#issues">вопросы</a></div>
{body}
<section class="take" id="sequence"><h2>{e(seq_t)}</h2><p>{e(seq_d)}</p>
<div class="grid" style="grid-template-columns:minmax(0,640px)">{video('Sequence_game.mp4', 'Игровая камера, общий план')}</div></section>
{numbers()}
<section class="take" id="issues"><h2>Известные ограничения</h2><div class="note"><ul>{issues}</ul></div></section>
</main><script>{JS}</script></body></html>"""


if __name__ == "__main__":
    (OUT / "index.html").write_text(page(), encoding="utf-8")
    manifest = {"stage": "animation_r01_owner_review", "owner_review_pending": True, "fps": 30,
                "takes": exp["takes"], "validation_all_pass": val["all_pass"], "fbx_roundtrip_all_pass": ver["all_pass"],
                "fbx": "../../animation/unity_package/ForestSplitter.fbx", "issues": tx.ISSUES,
                "videos": {t: [t + "_game.mp4", t + "_side.mp4"] for t in ORDER} | {"Sequence": ["Sequence_game.mp4"]}}
    (OUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print("PAGE_DONE", OUT / "index.html")
