"""Export the Unity package: ForestThorncaster.fbx (mesh + armature + all takes) and export.json.

blender -b -P build_package.py
Source: ../ForestThorncaster_Anim_r01.blend (read-only input; nothing is saved back into it).
Takes come from the NLA strips, so each FBX take is named exactly like its strip (ForestThorncaster_Idle, ...).
"""
import hashlib
import json
import shutil
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
A = HERE.parent
sys.path.insert(0, str(A))
from anim_apply import ORDER, PREFIX  # noqa: E402

SRC = A / "ForestThorncaster_Anim_r01.blend"
FBX = HERE / "ForestThorncaster.fbx"
bpy.ops.wm.open_mainfile(filepath=str(SRC))
scene = bpy.context.scene
arm = bpy.data.objects["ARM_ForestThorncaster"]
mesh = bpy.data.objects["SM_ForestThorncaster_LOD0"]
build = json.loads((A / "build.json").read_text())
val = json.loads((A / "validation.json").read_text())

# projectile socket: an empty on the tip of the right arm-spike (bone-parented objects sit on the bone tail)
muzzle = bpy.data.objects.new("Muzzle_RightSpike", None)
scene.collection.objects.link(muzzle)
muzzle.parent = arm
muzzle.parent_type = "BONE"
muzzle.parent_bone = "RightHand"
muzzle.location = (0, 0, 0)
muzzle.empty_display_size = 0.08

# make sure every take is an unmuted strip named exactly like the take
arm.animation_data.action = None
names = {st.name for tr in arm.animation_data.nla_tracks for st in tr.strips}
assert names == {PREFIX + n for n in ORDER}, names
for tr in arm.animation_data.nla_tracks:
    tr.mute = False
scene.render.fps = 30
scene.render.fps_base = 1.0

bpy.ops.object.select_all(action="DESELECT")
for ob in (arm, mesh, muzzle):
    ob.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=str(FBX), use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
    axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False, mesh_smooth_type="FACE",
    add_leaf_bones=False, use_armature_deform_only=True, armature_nodetype="NULL",
    bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
    path_mode="STRIP", embed_textures=False)

tex_src = A.parent / "rig" / "textures"
tex_dst = HERE / "Textures"   # Unity looks for model textures by file name in a Textures folder next to the FBX
tex_dst.mkdir(exist_ok=True)
for p in tex_src.glob("ForestThorncaster_*.png"):
    shutil.copy2(p, tex_dst / p.name)

takes = {}
for n in ORDER:
    t = build["takes"][n]
    e = {"fbx_take": PREFIX + n, "frames": t["frames"], "frame_count": t["frames"][1] + 1, "seconds": t["frames"][1] / 30,
         "loop": t["loop"], "events": t["events"]}
    if n == "LineCast":
        e["contact_frame"] = t["contact"]
        e["contact_meaning"] = "both spike tips touch the ground (z=0) in front; sim: first ground spike erupts at tick 27"
        e["spike_tip_ground_points_blender"] = t["ground_targets"]
    if n == "Burst":
        e["release_frame"] = t["contact"]
        e["release_meaning"] = "explosive star pose (sim windup 21 ticks)"
    if n == "Shot":
        e["release_frame"] = t["contact"]
        e["release_meaning"] = "thorn leaves the right arm-spike tip (sim windup 21 ticks)"
        e["muzzle"] = dict(val["takes"]["Shot"]["muzzle"], socket_object="Muzzle_RightSpike (child of bone RightHand, at its tail)")
    if n == "Walk":
        e["speed_mps"], e["cycle_m"] = t["speed_mps"], t["cycle_m"]
        e["planted_claw_slip_per_stance_m_est"] = val["takes"]["Walk"]["planted_claw_slip_per_stance_m_est"]
    takes[PREFIX + n] = e
report = {
    "fbx": str(FBX), "source_blend": str(SRC), "source_sha256": hashlib.sha256(SRC.read_bytes()).hexdigest(),
    "fps": 30, "root_motion": False, "root": "armature object at origin, never animated; Hips XY sway only (in place)",
    "axes": "Blender -Y forward, Z up -> FBX axis_forward=-Z, axis_up=Y -> Unity +Z forward; Unity pos = (-x_b, z_b, -y_b)",
    "triangles": val["triangles"], "deform_bones": val["bones"], "takes": takes,
    "textures": sorted(p.name for p in tex_dst.glob("*.png")),
    "verification": "verification.json (independent re-import check of this FBX; scripts in verify/, rerun verify/rebuild_and_verify.sh)",
    "unity_notes": ["Rig is an A-pose Meshy skeleton; Humanoid mapping in rig/rig_report.json (Spine02 is the lowest spine bone)",
                    "ORM map is not linked by the FBX material; connect textures/ForestThorncaster_ORM.png by hand",
                    "Generic rig works as-is; no root motion curves"],
}
(HERE / "export.json").write_text(json.dumps(report, indent=1, ensure_ascii=False), encoding="utf-8")
print("PACKAGE_OK", FBX)
