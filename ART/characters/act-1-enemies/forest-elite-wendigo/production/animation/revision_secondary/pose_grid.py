"""Read-only skull/antler separation study on the final collapse frame."""

import math
import runpy
import sys
from pathlib import Path
from types import SimpleNamespace

import bpy
from mathutils import Quaternion, Vector

out = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
out.mkdir(parents=True, exist_ok=True)
preview = runpy.run_path(str(Path(__file__).resolve().parents[2] / "preview" / "render_clip_review.py"))
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH" and o.find_armature() == rig)
original_action = bpy.data.actions["AN_ForestWendigo_Death"]
rig.animation_data.action = original_action
scene.frame_set(60)
args = SimpleNamespace(width=700, height=700, no_floor=False, front=True)
cameras, _ = preview["setup_review_scene"](scene, [mesh], args)
rest = {b.name: b.bone.matrix_local.copy() for b in rig.pose.bones}


def turn(name, axis, deg):
    b = rig.pose.bones[name]
    axes = rest[name].to_3x3()
    quat = Quaternion(Vector(axis), math.radians(deg))
    local = (axes.inverted() @ quat.to_matrix() @ axes).to_quaternion()
    b.rotation_quaternion = b.rotation_quaternion @ local


cases = {
    "A_baseline": {},
    "B_head_xp65": {"head_x": 65},
    "C_head_xp90": {"head_x": 90},
    "D_neck_xp35_head_xp45": {"neck_x": 35, "head_x": 45},
    "E_sp1xp20_sp2xp15_hxp50": {"sp1_x": 20, "sp2_x": 15, "head_x": 50},
    "F_sp1xp20_sp2xp15_hxp70": {"sp1_x": 20, "sp2_x": 15, "head_x": 70},
    "G_sp1xp20_sp2xp15_nxp30_hxp40": {"sp1_x": 20, "sp2_x": 15, "neck_x": 30, "head_x": 40},
    "H_sp1xp20_sp2xp15_nxp40_hxp50_pz-10": {"sp1_x": 20, "sp2_x": 15, "neck_x": 40, "head_x": 50, "pelvis_z": -0.1},
}

for name, offsets in cases.items():
    trial = original_action.copy()
    trial.name = "POSE_STUDY_" + name
    rig.animation_data.action = trial
    scene.frame_set(60)
    rig.pose.bones["pelvis"].location += rest["pelvis"].to_3x3().inverted() @ Vector((0, 0, offsets.get("pelvis_z", 0)))
    turn("spine_01", (1, 0, 0), offsets.get("sp1_x", 0))
    turn("spine_01", (0, 0, 1), offsets.get("sp1_yaw", 0))
    turn("spine_02", (1, 0, 0), offsets.get("sp2_x", 0))
    turn("spine_02", (0, 0, 1), offsets.get("sp2_yaw", 0))
    turn("neck", (1, 0, 0), offsets.get("neck_x", 0))
    turn("head", (1, 0, 0), offsets.get("head_x", 0))
    turn("head", (0, 0, 1), offsets.get("head_yaw", 0))
    turn("head", (0, 1, 0), offsets.get("head_roll", 0))
    rig.pose.bones["CTRL_R_knee"].location.x += 0.4 if name != "A_baseline" else 0
    rig.pose.bones["pelvis"].keyframe_insert(data_path="location", frame=60)
    for bone_name in ("spine_01", "spine_02", "neck", "head"):
        rig.pose.bones[bone_name].keyframe_insert(data_path="rotation_quaternion", frame=60)
    rig.pose.bones["CTRL_R_knee"].keyframe_insert(data_path="location", frame=60)
    scene.frame_set(60)
    bpy.context.view_layer.update()
    for view, camera in cameras.items():
        preview["render_frame"](scene, camera, out / f"{name}_{view}.png")
    evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mm = evaluated.to_mesh()
    min_z = min((evaluated.matrix_world @ v.co).z for v in mm.vertices)
    evaluated.to_mesh_clear()
    print("POSE_CASE", name, "MIN_Z", round(min_z, 4))
