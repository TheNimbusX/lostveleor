"""Derived Unity package: ONE FBX with mesh + deform skeleton + every take.
blender -b ForestRootSnarer_Anim_r01.blend -P build_package.py
The authored scene is a read-only input; the clean copy is saved as ForestRootSnarer_Package.blend.
Takes go out as NLA strips so the FBX take names are exactly the strip names (the all-actions
mode would prefix them with the armature name)."""
import json, shutil, sys
from pathlib import Path
import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_core  # noqa: E402

PKG = HERE / "unity_package"
TEX_SRC = HERE.parent / "rig" / "textures"
TEX_DST = PKG / "textures"
ORDER = ["ForestRootSnarer_Idle", "ForestRootSnarer_Walk", "ForestRootSnarer_Slam",
         "ForestRootSnarer_Hit", "ForestRootSnarer_Death",
         "ForestRootSnarer_Mend"]   # 27.09: appended last so the r01 strips keep their NLA frames
PKG.mkdir(exist_ok=True)
TEX_DST.mkdir(exist_ok=True)

sc = bpy.context.scene
sc.render.fps, sc.render.fps_base = 30, 1.0
arm = bpy.data.objects[anim_core.ARM_NAME]
mesh = bpy.data.objects[anim_core.MESH_NAME]
for ob in list(sc.objects):
    if ob not in (arm, mesh):
        bpy.data.objects.remove(ob, do_unlink=True)

# --- clean skeleton: drop IK constraints and the non-deforming helper bones
for pb in arm.pose.bones:
    for c in list(pb.constraints):
        pb.constraints.remove(c)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
removed = [eb.name for eb in arm.data.edit_bones   # keep root: non-deforming parent of pelvis
           if not eb.use_deform and not any(c.use_deform for c in eb.children_recursive)]
for name in removed:
    arm.data.edit_bones.remove(arm.data.edit_bones[name])
bpy.ops.object.mode_set(mode="OBJECT")
for pb in arm.pose.bones:
    pb.matrix_basis.identity()

# --- actions: keep only the package takes, drop fcurves of removed bones
for act in list(bpy.data.actions):
    if act.name not in ORDER:
        bpy.data.actions.remove(act)
bones = {b.name for b in arm.data.bones}
for act in bpy.data.actions:
    for lay in act.layers:
        for st in lay.strips:
            for bag in st.channelbags:
                for fc in list(bag.fcurves):
                    bn = fc.data_path.split('"')[1] if '"' in fc.data_path else None
                    if bn not in bones or fc.data_path.endswith(".scale"):
                        bag.fcurves.remove(fc)

# --- NLA layout: one track per take, strips end-to-end with 10-frame gaps
ad = arm.animation_data
ad.action = None
for tr in list(ad.nla_tracks):
    ad.nla_tracks.remove(tr)
start, layout = 0, {}
for name in ORDER:
    act = bpy.data.actions[name]
    f0, f1 = (int(round(v)) for v in act.frame_range)
    tr = ad.nla_tracks.new()
    tr.name = name
    st = tr.strips.new(name, start, act)
    st.name = name
    st.action_frame_start, st.action_frame_end = f0, f1
    st.frame_end = start + (f1 - f0)
    st.extrapolation = "NOTHING"
    layout[name] = [start, start + (f1 - f0)]
    start += (f1 - f0) + 10

# --- textures: Unity-side copies next to the FBX, relative paths
for png in TEX_SRC.glob("*.png"):
    shutil.copy2(png, TEX_DST / png.name)
for img in bpy.data.images:
    if img.source == "FILE":
        p = TEX_DST / Path(bpy.path.abspath(img.filepath)).name
        if p.exists():
            img.filepath = str(p)
            img.reload()

sc.frame_start, sc.frame_end = 0, start
sc.frame_set(0)
arm["root_motion"] = "none: clips are in place; the Sim moves the entity"
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "ForestRootSnarer_Package.blend"), relative_remap=True)

bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = arm
fbx = PKG / "ForestRootSnarer.fbx"
bpy.ops.export_scene.fbx(
    filepath=str(fbx), use_selection=True, object_types={"ARMATURE", "MESH"},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE",
    axis_forward="-Z", axis_up="Y", use_mesh_modifiers=True, mesh_smooth_type="FACE",
    use_tspace=False, add_leaf_bones=False, use_armature_deform_only=True, armature_nodetype="NULL",
    bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True,
    bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
    bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
    path_mode="RELATIVE", embed_textures=False)
(HERE / "work" / "package_layout.json").write_text(json.dumps(
    {"fbx": str(fbx), "nla_layout": layout, "removed_helper_bones": removed,
     "deform_bones": [b.name for b in arm.data.bones],
     "textures": sorted(p.name for p in TEX_DST.glob("*.png"))}, indent=1))
print("PACKAGE_DONE", fbx, flush=True)
