"""Mesh-level validation of the baked takes -> validation.json.

blender -b ForestThorncaster_Anim_r01.blend -P validate_anim.py
"""
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_eval as ae  # noqa: E402
from anim_apply import ORDER, PREFIX, front_contacts  # noqa: E402
from takes_loco import WALK_DUTY, WALK_FRAMES, WALK_SPEED  # noqa: E402

arm = bpy.data.objects["ARM_ForestThorncaster"]
mesh = bpy.data.objects["SM_ForestThorncaster_LOD0"]
spike = ae.rigid(mesh, "LeftHand") | ae.rigid(mesh, "RightHand")
rest = np.array([v.co[:] for v in mesh.data.vertices])
cf = front_contacts(mesh)
claw = {s: int(np.argmin(np.linalg.norm(rest - np.array(cf[s]), axis=1))) for s in cf}
deform = [b.name for b in arm.data.bones if b.use_deform]
V = {"triangles": sum(len(p.vertices) - 2 for p in mesh.data.polygons), "bones": len(deform), "takes": {}}


def bone_len_err():
    e = 0.0
    for b in arm.pose.bones:
        e = max(e, abs((b.tail - b.head).length / b.bone.length - 1))
    return e


for name in ORDER:
    act = bpy.data.actions[PREFIX + name]
    ae.use_action(arm, act)
    n = int(act.frame_end)
    r = {"frames": [0, n], "min_z_body": 9.0, "min_z_all": 9.0, "bone_length_err_max": 0.0,
         "hips_xy_max": 0.0, "armature_object_moved": False}
    per = {}
    for f in range(n + 1):
        ae.goto(f)
        co = ae.mesh_co(mesh)
        per[f] = co
        r["min_z_all"] = min(r["min_z_all"], float(co[:, 2].min()))
        if float(co[~spike, 2].min()) < r["min_z_body"]:
            r["min_z_body_frame"] = f
        r["min_z_body"] = min(r["min_z_body"], float(co[~spike, 2].min()))
        r["bone_length_err_max"] = max(r["bone_length_err_max"], bone_len_err())
        h = arm.pose.bones["Hips"].head
        r["hips_xy_max"] = max(r["hips_xy_max"], math.hypot(h.x, h.y))
        if arm.matrix_world.translation.length > 1e-6:
            r["armature_object_moved"] = True
    if name in ("Idle", "Walk"):
        r["loop_seam_vertex_m"] = float(np.linalg.norm(per[0] - per[n], axis=1).max())
        v0 = per[1] - per[0]
        v1 = per[n] - per[n - 1]
        r["loop_velocity_jump_m_per_frame"] = float(np.linalg.norm(v0 - v1, axis=1).max())
        dom = ae.dominant(mesh)
        worst = (0.0, 0, "")
        for f in range(n):
            acc = np.linalg.norm(per[(f + 1) % n] - 2 * per[f] + per[(f - 1) % n], axis=1)
            i = int(acc.argmax())
            if acc[i] > worst[0]:
                worst = (float(acc[i]), f, str(dom[i]))
        r["max_accel_m_per_frame2"] = {"value": worst[0], "frame": worst[1], "bone": worst[2]}
    if name == "Walk":
        step = WALK_SPEED / 30.0
        slip, zmax = 0.0, 0.0
        for s, off in (("Left", 0.0), ("Right", 0.5)):
            vi = claw[s]
            for f in range(n):
                u0, u1 = ((f / WALK_FRAMES) + off) % 1, (((f + 1) / WALK_FRAMES) + off) % 1
                if u0 < WALK_DUTY and u1 < WALK_DUTY and u1 > u0:
                    d = per[f + 1][vi] - per[f][vi]
                    slip = max(slip, float(np.linalg.norm(d - np.array([0, step, 0]))))
                    zmax = max(zmax, float(per[f][vi][2]))
        r["planted_claw_slip_per_frame_m"] = slip
        r["planted_claw_slip_per_stance_m_est"] = slip * WALK_DUTY * WALK_FRAMES
        r["planted_claw_height_max_m"] = zmax
        r["speed_mps"], r["cycle_m"] = WALK_SPEED, WALK_SPEED * WALK_FRAMES / 30
    if name == "LineCast":
        tz = {}
        for f in range(n + 1):
            ae.goto(f)
            tz[f] = (ae.tail(arm, "LeftHand")[2], ae.tail(arm, "RightHand")[2])
        first = min((f for f in tz if min(tz[f]) <= 0.005), default=None)
        r["spike_tip_first_ground_frame"] = first
        r["spike_tip_z_22_26"] = {f: [round(float(z), 4) for z in tz[f]] for f in range(22, 27)}
        r["spike_tips_buried_24_60_max_z"] = float(max(max(tz[f]) for f in range(24, 61)))
        ae.goto(30)
        a = [ae.tail(arm, b) for b in ("LeftHand", "RightHand")]
        ae.goto(60)
        b_ = [ae.tail(arm, b) for b in ("LeftHand", "RightHand")]
        r["spike_tip_drift_hold_30_60_m"] = float(max(np.linalg.norm(a[i] - b_[i]) for i in range(2)))
    if name == "Burst":
        spread = {}
        for f in range(n + 1):
            ae.goto(f)
            spread[f] = float(np.linalg.norm(ae.tail(arm, "LeftHand") - ae.tail(arm, "RightHand")))
        r["tip_spread_m"] = {"f18": spread[18], "f21": spread[21], "max_frame": max(spread, key=spread.get), "max": max(spread.values())}
    if name == "Shot":
        c = int(act.get("contact_frame", 21))
        ae.goto(c)
        pb = arm.pose.bones["RightHand"]
        tip = ae.tail(arm, "RightHand")
        d = np.array((pb.tail - pb.head).normalized())
        ae.goto(c - 1)
        prev = ae.tail(arm, "RightHand")
        r["muzzle"] = {"bone": "RightHand", "bone_local_offset_blender": [0.0, round(pb.bone.length, 4), 0.0],
                       "note": "tail of RightHand = tip of the right arm-spike; bone local +Y runs wrist->tip",
                       "world_blender_at_release": [round(float(x), 4) for x in tip],
                       "world_unity_at_release": [round(-float(tip[0]), 4), round(float(tip[2]), 4), round(-float(tip[1]), 4)],
                       "spike_dir_blender": [round(float(x), 4) for x in d],
                       "spike_dir_unity": [round(-float(d[0]), 4), round(float(d[2]), 4), round(-float(d[1]), 4)],
                       "tip_speed_into_release_mps": round(float(np.linalg.norm(tip - prev)) * 30, 2)}
    if name == "Death":
        last = per[n]
        r["final_min_z"] = float(last[:, 2].min())
        r["final_max_z"] = float(last[:, 2].max())
        r["final_body_low_z"] = {k: float(last[ae.dominant(mesh) == k, 2].min()) for k in ("Hips", "Spine01", "Head")}
        r["still_44_48_max_move_m"] = float(np.linalg.norm(per[n] - per[n - 4], axis=1).max())
    V["takes"][name] = r
    print("VALID", name, json.dumps(r)[:400], flush=True)
(HERE / "validation.json").write_text(json.dumps(V, indent=1))
print("VALIDATION_OK")
