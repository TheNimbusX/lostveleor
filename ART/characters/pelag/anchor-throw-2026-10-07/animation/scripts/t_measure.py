"""Замер источников по тикам (оси корня: f вперёд = −Y, l влево = +X, u вверх; метры рига Blender).
blender -b --factory-startup -P t_measure.py -- <out.json> <step> sabre:Pelag_AN_Sabre3 ...
"""
import bpy, sys, os, json, math
HERE = os.path.dirname(os.path.abspath(__file__))
PEL = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
for p in (os.path.join(PEL, "wreck-2026-10-03", "animation-v4", "scripts"), os.path.join(PEL, "wreck-2026-10-03", "animation-v3", "scripts")):
    if p not in sys.path: sys.path.insert(0, p)
import v4_lib as L
from wk_rig import Rig, SIDES
from s_lib import sagittal_lean
import wk_grip

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, STEP, SPECS = argv[0], float(argv[1]), argv[2:]
rig = Rig(); wk_grip.prepare(rig)
src = L.SabreSource(rig.dst)
fl = lambda v: [round(-v.y, 3), round(v.x, 3), round(v.z, 3)]
res = {}
for s in SPECS:
    clip = s.split(":")[1]
    n = {"Pelag_AN_Sabre1": 14, "Pelag_AN_Sabre2": 14, "Pelag_AN_Sabre3": 22}[clip]
    rows = []; t = 0.0
    while t <= n + 1e-6:
        rig.reset(); src.apply(clip, t); bpy.context.view_layer.update()
        rows.append(dict(t=t, hips=fl(rig.P("Hips")), lean=round(sagittal_lean(rig.dst), 1), pyaw=round(rig.pelvis_yaw(), 1),
                         cyaw=round(rig.chest_yaw(), 1), toe={x: fl(rig.P(x + "ToeBase")) for x in SIDES},
                         ankle={x: fl(rig.P(x + "Foot")) for x in SIDES}, hand={x: fl(rig.P(x + "Hand")) for x in SIDES},
                         sh={x: fl(rig.P(x + "Arm")) for x in SIDES}, head=fl(rig.P("Head"))))
        t += STEP
    res[clip] = rows
    print("MEASURED", clip, len(rows))
json.dump(res, open(OUT, "w"), indent=0)
