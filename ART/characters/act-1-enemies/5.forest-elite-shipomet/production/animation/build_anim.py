"""Author all Thorncaster takes onto the deform skeleton and save the animation master.

blender -b -P build_anim.py
-> ForestThorncaster_Anim_r01.blend (rig + 7 actions, one NLA track/strip per take named exactly like the FBX take)
-> build.json (IK reach, spike tips per frame, contact tips, muzzle)
"""
import json
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_apply as aa  # noqa: E402
from takes_attack import LINE_TIP, SHOT_DIR  # noqa: E402

OUT_BLEND = HERE / "ForestThorncaster_Anim_r01.blend"

arm, mesh, sk = aa.open_rig()
T = aa.takes(sk)
report = {"rig": str(aa.RIG_BLEND), "fps": 30, "contact_front_claws": sk.contact_front, "takes": {}}
actions = {}
for name in aa.ORDER:
    take = T[name]
    act, rep = aa.key_action(arm, sk, name, take)
    actions[name] = act
    info = {"frames": [0, take["frames"]], "loop": bool(take.get("loop")), "contact": take.get("contact"),
            "events": take.get("events", {}), "arm_ik_shortfall_max_m": round(rep["arm_short_max"], 4),
            "leg_ik_shortfall_max_m": round(rep["leg_short_max"], 4), "ik_short_frames": rep.get("short_frames", {})}
    if name == "LineCast":
        c = take["contact"]
        info["tips_at_contact"] = rep["tips"][c]
        info["tips_before_contact_min_z"] = min(min(rep["tips"][f]["L"][2], rep["tips"][f]["R"][2]) for f in range(c))
        info["ground_targets"] = LINE_TIP
    if name == "Shot":
        c = take["contact"]
        info["muzzle_world_at_release"] = rep["tips"][c]["R"]
        info["throw_dir_world"] = SHOT_DIR
    if name == "Walk":
        info["speed_mps"], info["cycle_m"] = take["speed_mps"], take["cycle_m"]
    report["takes"][name] = info
    print("TAKE", name, json.dumps({k: v for k, v in info.items() if k != "events"}), flush=True)

# one NLA track per take: the FBX exporter names each take after its strip
arm.animation_data.action = None
for tr in list(arm.animation_data.nla_tracks):
    arm.animation_data.nla_tracks.remove(tr)
for name in aa.ORDER:
    act = actions[name]
    tr = arm.animation_data.nla_tracks.new()
    tr.name = aa.PREFIX + name
    st = tr.strips.new(aa.PREFIX + name, 0, act)
    st.name = aa.PREFIX + name
    st.action_frame_start, st.action_frame_end = 0, act.frame_end
    st.extrapolation = "NOTHING"
    tr.mute = False
arm.animation_data.action = actions["Idle"]
arm["root_motion"] = "none: armature object never moves; the sim moves the entity"
arm["takes"] = json.dumps({n: [0, T[n]["frames"]] for n in aa.ORDER})
scene = bpy.context.scene
scene.render.fps = 30
scene.frame_start, scene.frame_end = 0, T["Idle"]["frames"]
scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT_BLEND))
(HERE / "build.json").write_text(json.dumps(report, indent=1))
print("BUILD_OK", OUT_BLEND)
