"""Крушение v3, Wait1/Wait2 (06.10): левая стопа висела ~4 см над землёй (нога кадра Swing1@12 / Swing2@19 прямая, до земли
не достаёт) — ставим её на землю, НЕ трогая ничего, кроме стопы: поворот кости LeftFoot вокруг лодыжки носком вниз, пока
нижняя точка подошвы не ляжет на пол (опора — подушечка, пятка чуть поднята), пальцы (LeftToeBase) — плашмя на полу.
Бедро, голень, таз, корпус, руки — ровно клип (хват и живой якорь те же). Поза ног в петле одна → стопа не едет.

blender -b --factory-startup -P v3s5_waitfoot.py -- <Pelag_AN_Wreck2_Wait1|Wait2> <out_dir>
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector, Quaternion
from s_lib import Body, rotate_world, foot_pitch
from wk_rig import Rig, M, SIDES
import wk_grip, wk_fingers, wk_check
from wk_sample import measure, big_bone_deltas
import v3_arms
import v3s4_patch
from v3s4_patch import RIG_
import v3s5_lib as L5

argv = sys.argv[sys.argv.index("--") + 1:]
CLIP, OUT = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)
N = 12
rig = Rig(); RIG_[0] = rig
wk_grip.prepare(rig)
body = Body(rig.mesh)
rig.reset(); wk_fingers.apply(rig, 0.0, 0.0); bpy.context.view_layer.update()
stance_snap = rig.snapshot()
L5.foot_local(rig, body)
base = L5.action_snaps(rig, CLIP, N)
LEG = [M("Left" + b) for b in ("UpLeg", "Leg", "Foot", "ToeBase", "Toe_End")] + [M("Hips")]
same = max(math.degrees(base[0][n][1].rotation_difference(base[f][n][1]).angle) for f in range(N + 1) for n in LEG)
print("LEGS constant over loop: max %.4f deg" % same)

rig.restore(base[0])
s = "Left"
F0 = L5.wmat(rig, s + "Foot").copy()
_, horiz = foot_pitch(rig.dst, s)
lat = L5.UP.cross(horiz).normalized()
ank = rig.P(s + "Foot").copy()
low0 = min((F0 @ v).z - h0 for v, h0 in L5.LOC[s].values())


def low_at(deg):
    F = L5.turn_about(F0, ank, Quaternion(lat, math.radians(deg)))      # + носок вниз (точки впереди лодыжки опускаются)
    return min((F @ v).z - h0 for v, h0 in L5.LOC[s].values())


lo, hi = 0.0, 40.0
for _ in range(40):
    mid = (lo + hi) / 2
    if low_at(mid) > 0.0: lo = mid
    else: hi = mid
ang = hi
L5.set_wmat(rig, s + "Foot", L5.turn_about(F0, ank, Quaternion(lat, math.radians(ang))))
toe = L5.toes_on_floor(rig, s)
fix = {n: rig.snapshot()[n] for n in (M("LeftFoot"), M("LeftToeBase"), M("LeftToe_End"))}
print("FOOT hang %.4f m -> toe down %.1f deg, toes bent %.1f deg" % (low0, ang, toe))

snaps, rows = [], []
for f in range(N + 1):
    sn = dict(base[f]); sn.update(fix)
    rig.restore(sn); snaps.append(rig.snapshot())
    row = measure(rig, body)
    row.update(frame=f, socket=[round(c, 4) for c in v3_arms.to_unity(v3_arms.socket(rig))], on_grip=True)
    rows.append(row)
src = json.load(open(os.path.join(L5.V3, CLIP + ".rows.json"), encoding="utf-8"))["rows"]
for f in range(N + 1):                                                  # хват в клипе тот же (мм)
    rows[f]["socket_vs_accepted"] = round(math.dist(rows[f]["socket"], src[f]["socket"]), 5)
    for k in ("sol", "on_grip", "seam_copy"):
        if k in src[f]: rows[f][k] = src[f][k]
print("SOCKET vs accepted max %.5f m" % max(r["socket_vs_accepted"] for r in rows))
summary, viol = wk_check.check(rig, body, {CLIP: snaps, "_stance": stance_snap}, {CLIP: rows})
wk_check.print_summary(summary, viol)
seams = {"f0_vs_f1": big_bone_deltas(snaps[0], snaps[1]), "f12_vs_f11": big_bone_deltas(snaps[12], snaps[11])}
json.dump(dict(rows=rows, foot_fix=dict(hang_before_m=round(low0, 4), toe_down_deg=round(ang, 1), toes_bent_deg=round(toe, 1)),
               seams={k: [round(v[0], 2), v[1]] for k, v in seams.items()},
               summary={k: {kk: (str(vv) if not isinstance(vv, (int, float)) else vv) for kk, vv in v.items()} for k, v in summary.items()},
               violations=[[a, b, str(c)] for a, b, c in viol]), open(os.path.join(OUT, CLIP + ".rows.json"), "w", encoding="utf-8"), indent=1)

dst = rig.dst
dst.animation_data_create()
act = bpy.data.actions.new(CLIP); act.use_fake_user = True
dst.animation_data.action = act
prev = {}
for f, snap in enumerate(snaps):
    for pb in dst.pose.bones:
        loc, q = snap[pb.name]; q = q.copy()
        if pb.name in prev and prev[pb.name].dot(q) < 0: q.negate()
        prev[pb.name] = q.copy()
        pb.location = loc; pb.rotation_quaternion = q
        pb.keyframe_insert("location", frame=f, group=pb.name)
        pb.keyframe_insert("rotation_quaternion", frame=f, group=pb.name)
for layer in act.layers:
    for strip in layer.strips:
        for bag in strip.channelbags:
            for fc in bag.fcurves:
                for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
bpy.ops.object.select_all(action='DESELECT')
dst.select_set(True); bpy.context.view_layer.objects.active = dst
dst.scale = rig.export_scale
bpy.context.view_layer.update()
sc = bpy.context.scene; sc.frame_start, sc.frame_end = 0, N
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, CLIP + ".fbx"), use_selection=True, object_types={'ARMATURE'},
                         add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                         bake_anim_step=1.0, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
bpy.ops.wm.save_as_mainfile(filepath=L5.blend_of(CLIP, OUT))
print("exported", CLIP)
