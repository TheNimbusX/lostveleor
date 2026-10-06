"""Абордаж v3 (03.10): новый Throw (замах через плечо 3 тика) + PullShort, Uppercut, Slam на том же конвейере.
Принятые Pull/Punch/Recover собираются теми же ключами (ab_keys.py) — только для стыков и сверки, не выдаются.

blender -b --factory-startup -P ab3_author.py -- <out_dir>
  AB_ONLY=<подстроки через запятую>, AB_NOEXPORT=1, AB_QUICK=1 (без пересечений), AB_DBG=1, AB_PREVIEW=<папка>
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from s_lib import Body, two_bone
from ab_rig import Rig, M, SIDES, fl
import ab3_pose as ab_pose, ab_arm, ab_check, ab_fingers, ab3_check
from ab3_keys import clips as clip_keys, DZ_FLOOR
from ab_measure import sample, measure, bone_deltas

OUT = sys.argv[sys.argv.index("--") + 1:][0]
os.makedirs(OUT, exist_ok=True)
ONLY = os.environ.get("AB_ONLY", "")
P = "Pelag_AN_Abordage2_"
NEED = {P + "Throw": (P + "Pull",), P + "PullShort": (P + "Pull", P + "Punch"), P + "Uppercut": (P + "Pull", P + "Punch"),
        P + "Slam": (P + "Pull", P + "Punch")}
DZ0 = ab_pose.DZ_MIN


def apply(rig, p, clip):
    ab_pose.DZ_MIN = DZ_FLOOR.get(clip, DZ0)
    try:
        return ab_pose.apply(rig, p)
    finally:
        ab_pose.DZ_MIN = DZ0


def snap_mix(sa, sb, t):
    """Кадр между двумя снимками: повороты костей — slerp (как Unity между ключами), сдвиги — по прямой."""
    out = {}
    for n, (la, qa) in sa.items():
        lb, qb = sb[n]
        if qa.dot(qb) < 0: qb = -qb
        out[n] = (la.lerp(lb, t), qa.slerp(qb, t))
    return out


GROUPS = {"armR": ("RightShoulder", "RightArm", "RightForeArm", "RightHand"),
          "armL": ("LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand"),
          "legs": ("LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase", "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase")}
rig = Rig()
ab_arm.prepare(rig)
ab_fingers.prepare(rig)
body = Body(rig.mesh)
B = rig.B
CLIPS = clip_keys(B)
stance_snap = rig.snapshot()
want = set(CLIPS) if not ONLY else {c for c in CLIPS if any(o in c for o in ONLY.split(","))}
for c in list(want): want |= set(NEED.get(c, ()))
snaps, rows, CACHE = {}, {}, {}
for name, keys in CLIPS.items():
    if name not in want: continue
    last = max(keys)
    for kf in sorted(keys):
        k = keys[kf]
        if "slerp" in k or k.get("fixed"): continue
        if k["W"] <= 1e-6: k["fixed"] = True; continue
        info = apply(rig, k, name)
        k["sv"], k["tv"], k["wd"], k["wf"] = info["swivel_twist"][0], float(info["swivel_twist"][1]), info["wd"], info["wf"]; k["fixed"] = True
    snaps[name], rows[name] = [], []
    for f in range(last + 1):
        k = keys.get(f)
        if k is not None and "slerp" in k:             # кадр-смесь двух кадров клипа по костям (досчёт ниже)
            snaps[name].append(None); rows[name].append(None); continue
        p = sample({kf: kv for kf, kv in keys.items() if "slerp" not in kv}, f)
        if k is not None and id(k) in CACHE:          # общая стыковая поза — ровно тот же снимок
            rig.restore(CACHE[id(k)])
        elif p["W"] <= 1e-6:
            rig.reset()
        else:
            apply(rig, p, name)
        if k is not None and id(k) not in CACHE:
            CACHE[id(k)] = rig.snapshot()
        snaps[name].append(rig.snapshot())
        row = measure(rig, body); row.update(frame=f, W=round(p["W"], 3))
        rows[name].append(row)
    for f in range(last + 1):                          # кости руки ключа — смесь двух кадров клипа (путь делится, без петли)
        k = keys.get(f)
        if k is None or "mix" not in k: continue
        rig.restore(snaps[name][f])
        for grp, fa, fb, t in k["mix"]:
            for bn in GROUPS[grp]:
                qa, qb = snaps[name][fa][M(bn)][1], snaps[name][fb][M(bn)][1]
                if qa.dot(qb) < 0: qb = -qb
                rig.dst.pose.bones[M(bn)].rotation_quaternion = qa.slerp(qb, t)
        bpy.context.view_layer.update()
        snaps[name][f] = rig.snapshot()
        if id(k) in CACHE: CACHE[id(k)] = snaps[name][f]
        row = measure(rig, body); row.update(frame=f, W=1.0, mix=[list(m) for m in k["mix"]])
        rows[name][f] = row
    for f in range(last + 1):
        k = keys.get(f)
        if k is None or "slerp" not in k: continue
        fa, fb, t = k["slerp"]
        tg = {}
        for sd in k.get("plant", ()):                  # опорная стопа смеси — точно на месте (смесь костей её чуть ведёт)
            rig.restore(snaps[name][fa]); a_ = rig.P(sd + "Foot").copy()
            rig.restore(snaps[name][fb]); b_ = rig.P(sd + "Foot").copy()
            tg[sd] = a_.lerp(b_, t)
        for sd, (af, al, au) in k.get("foot_at", {}).items():     # стопа смеси в своей точке (оси корня)
            tg[sd] = fl(af, al, au)
        rig.restore(snap_mix(snaps[name][fa], snaps[name][fb], t))
        for sd, tgt in tg.items():
            two_bone(rig.dst, [M(sd + "UpLeg"), M(sd + "Leg"), M(sd + "Foot")], tgt, keep_end=True)
        bpy.context.view_layer.update()
        snaps[name][f] = rig.snapshot()
        row = measure(rig, body); row.update(frame=f, W=1.0, slerp=[fa, fb, t])
        rows[name][f] = row
    print("built", name, last + 1)

if os.environ.get("AB_DBG"):
    BN = ("RightShoulder", "RightArm", "RightForeArm", "RightHand", "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
          "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot", "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head")
    for name, ss in snaps.items():
        for f in range(len(ss) - 1):
            d = ab_check.deltas(ss[f], ss[f + 1])
            print("DBG %s %2d>%2d %s" % (name[len(P):], f, f + 1, " ".join("%s:%.0f" % (b.replace("Right", "R").replace("Left", "L"), d[b]) for b in BN)))
    for name, ss in snaps.items():
        for lab, a, b, strike in ab3_check.ticks3(name, len(ss)):
            pts = [a] + [float(k) for k in range(int(math.floor(a)) + 1, int(math.ceil(b - 1e-9)))] + [b]
            sx = [ab_check.interp(ss, x) for x in pts]; d = {}
            for j in range(len(sx) - 1):
                for bn, ang in ab_check.deltas(sx[j], sx[j + 1]).items(): d[bn] = d.get(bn, 0.0) + ang
            arm_ = max((d[x], x) for x in ab_check.ARM_R); bod_ = max((v, k) for k, v in d.items() if k not in ab_check.ARM_R)
            if arm_[0] > (70 if strike else 35) or bod_[0] > 35 or "W3A6" in lab or lab.startswith("S5") or "→" in lab:
                print("DBGT %s %-9s %5.2f→%5.2f %s arm %.1f %s body %.1f %s" % (name[len(P):], lab, a, b, "S" if strike else "-", arm_[0], arm_[1], bod_[0], bod_[1]))

if os.environ.get("AB_FEETDBG"):
    mesh_ = ab_check.Mesh(rig, body)
    for name in [c for c in snaps if any(o in c for o in os.environ["AB_FEETDBG"].split(","))]:
        for x in [k * 0.25 for k in range(4 * (len(snaps[name]) - 1) + 1)]:
            vs = mesh_.at(ab_check.interp(snaps[name], x))
            for sd in SIDES:
                g = [vs[i] for i in mesh_.feet[sd] if vs[i].z < 0.012]
                if not g: continue
                cx = sum(v.x for v in g) / len(g); cy = sum(v.y for v in g) / len(g)
                if os.environ.get("AB_FEETV"):
                    vv = mesh_.at(ab_check.interp(snaps[name], 1.0)); bad_ = []
                    for i in mesh_.feet[sd]:
                        if vs[i].z < 0.012 and vv[i].z < 0.012:
                            dd = ((vs[i].x - vv[i].x) ** 2 + (vs[i].y - vv[i].y) ** 2) ** .5
                            if dd > 0.006: bad_.append((round(dd, 4), i, round(vs[i].z, 4), [round(c, 3) for c in vs[i]][:2]))
                    vs = mesh_.at(ab_check.interp(snaps[name], x))
                    if bad_: print("FEETV %s x%.2f %s %d %s" % (name[len(P):], x, sd, len(bad_), sorted(bad_)[-3:]))
                print("FEET %s x%.2f %s n%d c(%.4f,%.4f) toe(%.3f,%.3f) heel(%.3f,%.3f) ank %s" % (name[len(P):], x, sd, len(g), cx, cy,
                      rig.P(sd + "ToeBase").x, rig.P(sd + "ToeBase").y, min(v.y for v in g), max(v.y for v in g),
                      [round(c, 3) for c in rig.P(sd + "Foot")]))

# ------------------------------------------------------------- стыки (0°)
SEAMS = [("stance", P + "Throw"), (P + "Throw", P + "Pull"), (P + "Throw", P + "PullShort"), (P + "PullShort", P + "Punch"),
         (P + "Pull", P + "Punch"), (P + "Punch", P + "Recover"), (P + "Uppercut", P + "Recover"), (P + "Slam", P + "Recover"),
         (P + "Recover", "stance")]
MID = [(P + "Pull", 11, P + "Uppercut", 0), (P + "Pull", 11, P + "Slam", 0), (P + "PullShort", 4, P + "Uppercut", 0),
       (P + "PullShort", 4, P + "Slam", 0), (P + "PullShort", 3, P + "Pull", 10), (P + "PullShort", 5, P + "Pull", 12)]
seams = []
for a, b in SEAMS:
    if (a != "stance" and a not in snaps) or (b != "stance" and b not in snaps): continue
    sa = stance_snap if a == "stance" else snaps[a][-1]
    sb = stance_snap if b == "stance" else snaps[b][0]
    w = bone_deltas(sa, sb)
    seams.append(dict(from_clip=a, from_frame="last", to_clip=b, to_frame=0, max_bone_deg=round(w[0], 2), bone=w[1]))
for a, fa, b, fb in MID:
    if a not in snaps or b not in snaps: continue
    w = bone_deltas(snaps[a][fa], snaps[b][fb])
    seams.append(dict(from_clip=a, from_frame=fa, to_clip=b, to_frame=fb, max_bone_deg=round(w[0], 2), bone=w[1]))
for s in seams: print("seam %s[%s] -> %s[%s]: %.2f° %s" % (s["from_clip"][len(P):] if s["from_clip"] != "stance" else "stance", s["from_frame"],
                                                         s["to_clip"][len(P):] if s["to_clip"] != "stance" else "stance", s["to_frame"], s["max_bone_deg"], s["bone"]))

# ------------------------------------------------------------- пределы (только новые клипы; принятые — эталон)
NEW = [c for c in (P + "Throw", P + "PullShort", P + "Uppercut", P + "Slam") if c in snaps]
summary, viol = ab3_check.check3(rig, body, dict({c: snaps[c] for c in NEW}, _stance=stance_snap), quick=bool(os.environ.get("AB_QUICK")))
ab_check.print_summary(summary, viol)
per_tick = {}
for name in NEW:
    ss = snaps[name]
    per_tick[name] = [[lab, a, b, strike, max(ab_check.deltas(ab_check.interp(ss, a), ab_check.interp(ss, b)).items(), key=lambda kv: kv[1])]
                      for lab, a, b, strike in ab3_check.ticks3(name, len(ss))]
for c in summary.values():
    c.pop("rows", None)
out = dict(rows=rows, seams=seams, per_tick=per_tick, check=dict(summary=summary, violations=[[v[0], v[1], str(v[2])] for v in viol]))
p = os.path.join(OUT, "_measure3.json")
open(p, "a").close()
f = open(p, "r+", encoding="utf-8", newline=""); f.seek(0); f.write(json.dumps(out, indent=1, ensure_ascii=False)); f.truncate(); f.close()
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "ab_preview.py"), encoding="utf-8").read())
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "ab_export.py"), encoding="utf-8").read())
