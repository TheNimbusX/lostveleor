"""Крушение v3, «Якорная броня» (06.10): Swing1/Swing2/Slam_Braced — поза 10 поверх ПРИНЯТОЙ базы, хват и якорь те же.

blender -b --factory-startup -P v3s5_brace.py -- <braced clip> <out_dir>
  Конфиг клипа — v3s5_brace_keys.CFG[<clip>]. Кадры базы — из рабочего .blend базы (animation-v3), стыки (SEAM) — копия
  снимка уже собранного braced-клипа из V3S5_SEAMDIR (по умолчанию out_dir), IDENT — кадры ровно базы (выход в стойку).
  V3S5_CORR — {"corr": [[x,y,z] Unity, …]}: поправка переноса (выгрузка braced − выгрузка базы, копится по кругам);
  V3S5_NOEXPORT / V3S5_QUICK — как у v3s4_author.
Меняется: таз вниз (Δ ≤ потолка таза 0,495 и досягаемости рук), стопы (сдвиг шире, пятка ниже, «прибитые» стопы на землю),
колени наружу, подбородок вниз; кисти — ТОЧНО в мировые матрицы кистей базы (+ поправка), пальцы — базы.
"""
import bpy, sys, os, json, math, importlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector, Quaternion
from s_lib import Body, translate_world
from wk_rig import Rig, M, SIDES, fl
import wk_grip, wk_fingers, wk_check
from wk_sample import measure, big_bone_deltas
import v3_arms
import v3s4_patch
from v3s4_patch import RIG_
import v3s5_lib as L5
import v3s5_brace_keys as K

argv = sys.argv[sys.argv.index("--") + 1:]
CLIP, OUT = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)
C = K.CFG[CLIP]; N = C["N"]
ENV = lambda k: os.environ.get("V3S5_" + k)
CORR = json.load(open(ENV("CORR")))["corr"] if ENV("CORR") else [[0.0, 0.0, 0.0]] * (N + 1)
SEAMDIR = ENV("SEAMDIR") or OUT

rig = Rig(); RIG_[0] = rig
print("FIST", wk_grip.prepare(rig))
body = Body(rig.mesh)
rig.reset(); wk_fingers.apply(rig, 0.0, 0.0); bpy.context.view_layer.update()
stance_snap = rig.snapshot()
L5.foot_local(rig, body)
base = L5.action_snaps(rig, C["base"], N)
refs = {}
for side, (rc, rf) in C.get("pin_ref", {}).items():      # опорная стопа «прибита» к позе стопы базы в этом кадре
    rig.restore(L5.action_snaps(rig, rc, rf)[rf]); refs[side] = L5.foot_frame(rig, side)
seam = {f: L5.action_snaps(rig, sc, sf, SEAMDIR)[sf] for f, (sc, sf) in C.get("SEAM", {}).items()}

# ---- Δ таза: потолок смещения таза к привязке и досягаемость рук (кисти базы), потом сглаживание под потолком
cap = []
for f in range(N + 1):
    rig.restore(base[f])
    H = {s: L5.wmat(rig, s + "Hand").translation.copy() for s in SIDES}
    d = min(C.get("DMAX", 0.07), L5.drop_for_ratio(rig, C.get("RATIO", 0.495)))
    RE = C.get("REACH", 0.993)

    def ok(dd):
        rig.restore(base[f]); translate_world(rig.dst, rig.dst.pose.bones[M("Hips")], Vector((0, 0, -dd)))
        return max(L5.arm_reach(rig, s, H[s]) for s in SIDES) <= RE

    if ok(d): cap.append(d)
    elif not ok(0.0): cap.append(0.0)
    else:
        lo, hi = 0.0, d
        for _ in range(20):
            mid = (lo + hi) / 2
            if ok(mid): lo = mid
            else: hi = mid
        cap.append(lo)
BZ = []
for f in range(N + 1):
    rig.restore(base[f]); BZ.append(rig.P("Hips").z)
ZMIN = [BZ[f] - cap[f] for f in range(N + 1)]          # ниже нельзя (потолок таза / руки)
Z = list(ZMIN)
for _ in range(C.get("SMOOTH_PASSES", 40)):            # сглаживание высоты таза сверху: не ниже ZMIN
    Z = [min(BZ[f], max(ZMIN[f], (Z[max(0, f - 1)] + 2 * Z[f] + Z[min(N, f + 1)]) / 4)) for f in range(N + 1)]   # и не выше базы
D = [BZ[f] - Z[f] for f in range(N + 1)]
for f, w in C.get("EXIT_W", {}).items(): D[f] *= w
print("DROP cap", [round(x, 3) for x in cap]); print("DROP use", [round(x, 3) for x in D])

snaps, rows, info = [], [], []
for f in range(N + 1):
    if f in seam:
        rig.restore(seam[f]); snaps.append(rig.snapshot()); info.append("seam %s@%d" % C["SEAM"][f]); continue
    if f in C.get("IDENT", ()):
        rig.restore(base[f]); snaps.append(rig.snapshot()); info.append("base"); continue
    rig.restore(base[f])
    Hb = {s: L5.wmat(rig, s + "Hand").copy() for s in SIDES}
    poles = {s: L5.arm_pole(rig, s) for s in SIDES}
    ff = {s: L5.foot_frame(rig, s) for s in SIDES}
    translate_world(rig.dst, rig.dst.pose.bones[M("Hips")], Vector((0, 0, -D[f])))
    note = []
    for s in SIDES:
        spec = C["feet"](f, s)
        mode, (df, dl), k = spec["mode"], spec.get("off", (0.0, 0.0)), spec.get("k", 1.0)
        if mode in ("pin", "hover"):
            R = refs[side_ref] if (side_ref := spec.get("ref", s)) in refs else ff[s]
            dyaw = math.radians(((ff[s]["yaw"] - R["yaw"]) + 180) % 360 - 180)
            F = L5.turn_about(R["F"], R["ball"], Quaternion(L5.UP, dyaw))
            lat = (Quaternion(L5.UP, dyaw) @ R["lat"]).normalized(); ball = R["ball"].copy()
            F = L5.heel(F, ball, lat, max(0.0, ff[s]["pitch"]) * k)
            if mode == "hover":
                from mathutils import Matrix
                F = Matrix.Translation(Vector((0, 0, spec["h"]))) @ F; ball = ball + Vector((0, 0, spec["h"]))
        else:
            F, ball, lat = ff[s]["F"].copy(), ff[s]["ball"].copy(), ff[s]["lat"]
            if k < 1.0 and ff[s]["pitch"] > 0:
                F = L5.heel(F, ball, lat, -(1.0 - k) * ff[s]["pitch"])
                F, _ = L5.ground(F, s, rig.toe_floor)
        if mode == "blend":                                # к посадке: смесь пути базы и «прибитой» позы
            R = refs[spec.get("ref", s)]; w = spec["w"]
            Fp = L5.turn_about(R["F"], R["ball"], Quaternion(L5.UP, math.radians(((ff[s]["yaw"] - R["yaw"]) + 180) % 360 - 180)))
            loc = F.translation.lerp(Fp.translation, w); q = F.to_quaternion().slerp(Fp.to_quaternion(), w)
            F = q.to_matrix().to_4x4(); F.translation = loc; ball = ball.lerp(R["ball"], w)
        sh = L5.root_vec(df, dl)
        F = F.copy(); F.translation = F.translation + sh; ball = ball + sh
        turn, miss = L5.place_foot(rig, s, F, ball, lat, knee_out=C["knee"](f, s), pivot=spec.get("pivot", "auto"))
        if abs(turn) > 0.05 or miss > 0.002: note.append("%s %s%.0f° miss %.3f" % (s[0], "пятка+" if turn > 0 else "носок+", abs(turn), miss))
    cx = [0.0, 0.0, 0.0] if f in C.get("NOCORR", ()) else CORR[f]
    dv = v3_arms.from_unity(cx)
    for s in SIDES:
        H = Hb[s].copy(); H.translation = H.translation - dv
        m = L5.place_arm(rig, s, H, poles[s])
        if m > 0.002: note.append("%s arm miss %.3f" % (s[0], m))
    L5.nod(rig, C["chin"](f))
    bpy.context.view_layer.update()
    snaps.append(rig.snapshot()); info.append(" ".join(note))

for side, fr in C.get("LEG_INTERP", {}).items():       # нога в воздухе между двумя кадрами — смесью локальных поворотов
    names = [n for n in snaps[0] if any(n == M(side + b) for b in ("UpLeg", "Leg", "Foot", "ToeBase", "Toe_End"))]
    for f, (fa, fb, w) in fr.items():
        for n in names:
            (la, qa), (lb, qb) = snaps[fa][n], snaps[fb][n]
            if qa.dot(qb) < 0: qb = -qb
            snaps[f][n] = (la.lerp(lb, w), qa.slerp(qb, w))
        info[f] += " %s нога — смесь %d/%d" % (side[0], fa, fb)
for f, sn in enumerate(snaps):
    rig.restore(base[f]); bs = v3_arms.to_unity(v3_arms.socket(rig))
    rig.restore(sn)
    row = measure(rig, body)
    sock = v3_arms.to_unity(v3_arms.socket(rig))
    row.update(frame=f, socket=[round(c, 4) for c in sock], base_socket=[round(c, 4) for c in bs], on_grip=True,
               socket_vs_base=round(math.dist(sock, [b - c for b, c in zip(bs, CORR[f])]), 4), drop=round(D[f], 4),
               hip=round(L5.hip_ratio(rig), 3), seam_copy=f in seam, note=info[f])
    rows.append(row)
    print("ROW %2d drop %.3f hz %.3f hip %.3f sock-base %.4f lean %5.1f tw %5.1f relP %5.1f gap %.3f fore %s feet L %.4f R %.4f %s" % (
        f, D[f], rig.P("Hips").z, row["hip"], row["socket_vs_base"], row["lean"], row["twist"], row["relP"], row["hand_gap"],
        row["forearm_twist"], row["Left"]["toe_z"], row["Right"]["toe_z"], info[f]))

seams = {}
for f in seam:
    nb = f + 1 if f + 1 <= N else f - 1
    seams["f%d_vs_f%d" % (f, nb)] = big_bone_deltas(snaps[f], snaps[nb])
print("SEAM neighbours", {k: (round(v[0], 1), v[1]) for k, v in seams.items()})
wk_check.STRIKE[CLIP] = C["STRIKE"]
summary, viol = wk_check.check(rig, body, {CLIP: snaps, "_stance": stance_snap}, {CLIP: rows}, quick=bool(ENV("QUICK")))
wk_check.print_summary(summary, viol)
json.dump(dict(base=C["base"], rows=rows, drop=[round(x, 4) for x in D], drop_cap=[round(x, 4) for x in cap],
               seams={k: [round(v[0], 2), v[1]] for k, v in seams.items()},
               summary={k: {kk: (str(vv) if not isinstance(vv, (int, float)) else vv) for kk, vv in v.items()} for k, v in summary.items()},
               violations=[[a, b, str(c)] for a, b, c in viol]), open(os.path.join(OUT, CLIP + ".rows.json"), "w", encoding="utf-8"), indent=1)

if not ENV("NOEXPORT"):
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
