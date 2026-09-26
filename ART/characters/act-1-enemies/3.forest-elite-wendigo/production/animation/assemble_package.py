"""Combine approved-stage Wendigo clips and bake an engine exchange FBX.

Run in Blender 5.2 with Wendigo_Primary.blend open. The secondary .blend path
is passed after `--`. Only the two character objects are exported; IK controls
are baked into deform bones and omitted from the FBX skeleton.
"""

import json
import sys
from pathlib import Path

import bpy


ROOT = Path(__file__).resolve().parent
secondary = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
if not secondary.is_file():
    raise FileNotFoundError(secondary)

rig = bpy.data.objects["ARM_ForestWendigo"]
mesh = bpy.data.objects["SM_ForestWendigo_LOD0"]
names = (
    "AN_ForestWendigo_Idle",
    "AN_ForestWendigo_Walk",
    "AN_ForestWendigo_Claw",
    "AN_ForestWendigo_Leap",
    "AN_ForestWendigo_Hit",
    "AN_ForestWendigo_Death",
)
with bpy.data.libraries.load(str(secondary), link=False) as (src, dst):
    missing = sorted(set(names[0:2] + names[4:]) - set(src.actions))
    if missing:
        raise RuntimeError(f"Missing secondary actions: {missing}")
    dst.actions = list(names[0:2] + names[4:])

scene = bpy.context.scene
scene.render.fps = 30
rig.animation_data_create()
pose_spans = {}
for name in names:
    action = bpy.data.actions[name]
    action.use_fake_user = True
    rig.animation_data.action = action
    first, last = (int(round(v)) for v in action.frame_range)
    scene.frame_set(first)
    bpy.context.view_layer.update()
    a = tuple((rig.matrix_world @ rig.pose.bones["head"].matrix).translation)
    scene.frame_set((first + last) // 2)
    bpy.context.view_layer.update()
    b = tuple((rig.matrix_world @ rig.pose.bones["head"].matrix).translation)
    pose_spans[name] = {
        "range": [first, last],
        "head_start_to_middle_m": round(sum((x - y) ** 2 for x, y in zip(a, b)) ** .5, 4),
    }
    if last <= first:
        raise RuntimeError(f"Action has no time span: {name}")

rig.animation_data.action = bpy.data.actions[names[0]]
scene.frame_set(0)
source = ROOT / "ForestWendigo_Animated.blend"
bpy.ops.wm.save_as_mainfile(filepath=str(source))

bpy.ops.object.select_all(action="DESELECT")
rig.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = rig
fbx = ROOT / "ForestWendigo_Animated.fbx"
bpy.ops.export_scene.fbx(
    filepath=str(fbx),
    use_selection=True,
    object_types={"ARMATURE", "MESH"},
    global_scale=1,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_UNITS",
    axis_forward="-Z",
    axis_up="Y",
    use_mesh_modifiers=True,
    mesh_smooth_type="FACE",
    add_leaf_bones=False,
    use_armature_deform_only=True,
    armature_nodetype="NULL",
    bake_anim=True,
    bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=True,
    bake_anim_force_startend_keying=True,
    bake_anim_step=1,
    bake_anim_simplify_factor=0,
    path_mode="ABSOLUTE",
    embed_textures=False,
)
report = ROOT / "assembly_report.json"
report.write_text(json.dumps({
    "blend": str(source), "fbx": str(fbx), "secondary": str(secondary),
    "fps": 30, "actions": pose_spans,
    "mesh_faces": len(mesh.data.polygons),
    "deform_bones": sum(bool(b.use_deform) for b in rig.data.bones),
}, indent=2), encoding="utf-8")
print("WENDIGO_PACKAGE_READY", source, fbx, report)
