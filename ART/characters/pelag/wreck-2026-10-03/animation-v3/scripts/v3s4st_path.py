"""Крушение v3, Stow на новое крепление: путь гнезда левой кисти в осях корня Unity из точек в осях груди (v3s4st_keys.STOW_PTS).
blender -b --factory-startup -P v3s4st_path.py -- <seam grip x,y,z> <out path.json>
Тело ставится ключами v3s4st_keys (wk_pose.body с поправками v3s4_patch, как в авторе); оси груди: вправо = RightArm − LeftArm,
вверх = Neck − Spine2 (ортогонально), назад = вправо × вверх; начало — Spine2."""
import bpy, sys, os, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wk_rig import Rig
import wk_grip, wk_pose, v3_arms
import v3s4_patch
from v3s4_patch import RIG_
import v3s4st_keys as K

argv = sys.argv[sys.argv.index("--") + 1:]
g0 = [float(x) for x in argv[0].split(",")]; OUT = argv[1]
rig = Rig(); RIG_[0] = rig; wk_grip.prepare(rig)
frames, chest = [g0], {}
for f in range(1, K.N + 1):
    wk_pose.body(rig, K.body_at(f)); bpy.context.view_layer.update()
    o = rig.P("Spine2"); r = (rig.P("RightArm") - rig.P("LeftArm")).normalized()
    u = rig.P("Neck") - o; u = (u - u.dot(r) * r).normalized(); b = r.cross(u).normalized()
    off = K.STOW_PTS.get(f, K.STOW_PTS[K.HAND])
    w = o + (r * off[0] + u * off[1] + b * off[2]) / v3_arms.K
    frames.append([round(c, 5) for c in v3_arms.to_unity(w)])
    chest[f] = dict(o=[round(c, 5) for c in v3_arms.to_unity(o)])
json.dump(dict(frames=frames, note="Stow: левая — точки STOW_PTS в осях груди (v3s4st_keys), кадр 0 — стык Wait1@0"), open(OUT, "w"), indent=1)
print("STOWPATH", OUT, frames[K.HAND])
