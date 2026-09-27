"""Author every take onto the deform skeleton (one key per frame, location + quaternion) and save
the working scene. blender -b <rig.blend> -P build_anim.py -- <out.blend> [take ...]
Writes work/build_report.json (per-frame solver numbers) next to this script."""
import sys, json
from pathlib import Path
import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_core  # noqa: E402
import clip_idle_hit, clip_walk, clip_slam, clip_death, clip_mend  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
out = Path(argv[0]).resolve()
only = set(argv[1:])
TAKES = {}
for m in (clip_idle_hit, clip_walk, clip_slam, clip_death, clip_mend):
    TAKES.update(m.TAKES)

sc = bpy.context.scene
sc.render.fps = 30
sc.render.fps_base = 1.0
arm = bpy.data.objects[anim_core.ARM_NAME]
mesh = bpy.data.objects[anim_core.MESH_NAME]
rig = anim_core.Rig(arm, mesh)
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"
    for c in pb.constraints:
        c.influence = 0.0
arm.animation_data_create()
report = {}
for name, take in TAKES.items():
    if only and name not in only:
        continue
    if "prepare" in take:
        take["prepare"](rig)
    n = take["frames"]
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    prev = {}
    frames = []
    for f in range(n + 1):
        M, info = rig.solve(take["pose"](float(f)))
        B = rig.basis(M)
        for bn in rig.keyed:
            pb = arm.pose.bones[bn]
            q = B[bn].to_quaternion()
            if bn in prev and q.dot(prev[bn]) < 0:
                q.negate()
            prev[bn] = q
            pb.location = B[bn].to_translation()
            pb.rotation_quaternion = q
            pb.keyframe_insert("location", frame=f, group=bn)
            pb.keyframe_insert("rotation_quaternion", frame=f, group=bn)
        frames.append({"f": f, "reach_err": {k: round(v, 4) for k, v in info["reach_err"].items() if v > 1e-4},
                       "slab_min_z": {k: round(v, 4) for k, v in info["slab_min_z"].items()},
                       "socket": {s: [round(c, 4) for c in M[f"{s}_hand"].translation] for s in "LR"},
                       "ankle": {s: [round(c, 4) for c in M[f"{s}_foot"].translation] for s in "LR"},
                       "pelvis": [round(c, 4) for c in M["pelvis"].translation]})
    for lay in act.layers:
        for st in lay.strips:
            for bag in st.channelbags:
                for fc in bag.fcurves:
                    for kp in fc.keyframe_points:
                        kp.interpolation = "LINEAR"
    act.use_frame_range = True
    act.frame_start, act.frame_end = 0, n
    act.use_cyclic = bool(take["loop"])
    act["loop"] = bool(take["loop"])
    for k, v in take.get("events", {}).items():
        act[f"event_{k}"] = json.dumps(v)
    report[name] = {"frames": n, "loop": take["loop"], "events": take.get("events", {}),
                    "max_reach_err": max((max(fr["reach_err"].values(), default=0) for fr in frames), default=0),
                    "per_frame": frames}
    if name == "ForestRootSnarer_Death":
        report[name]["floor_pelvis_drop_m"] = clip_death.INFO.get("floor_pelvis_drop_m")
    if name == "ForestRootSnarer_Slam":
        report[name]["face_clearance"] = clip_slam.INFO.get("face_clearance")
    if name == "ForestRootSnarer_Mend":
        report[name].update(clip_mend.INFO)
    print("TAKE", name, n, "max_reach_err", report[name]["max_reach_err"], flush=True)

arm.animation_data.action = bpy.data.actions.get("ForestRootSnarer_Idle")
sc.frame_start, sc.frame_end = 0, 60
sc.frame_set(0)
(HERE / "work").mkdir(exist_ok=True)
prev_rep = HERE / "work" / "build_report.json"
merged = json.loads(prev_rep.read_text()) if (only and prev_rep.exists()) else {}
merged.update(report)
prev_rep.write_text(json.dumps(merged, indent=1))
bpy.ops.wm.save_as_mainfile(filepath=str(out), relative_remap=True)
print("BUILD_DONE", out, flush=True)
