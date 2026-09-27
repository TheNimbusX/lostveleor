"""Export the Unity FBX (deform bones + skinned mesh, no animation) from ForestSplitter_Rig.blend.

usage: blender -b ForestSplitter_Rig.blend -P export_rig.py -- <out_dir>
Textures are written next to the FBX (textures/*.png); the master .blend keeps them packed.
Axes follow the Stonehoof/Wendigo packages: axis_forward -Z, axis_up Y (Blender -Y front -> Unity +Z).
"""
import json
import re
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import pose_lib  # noqa: E402
import rig_spec as spec  # noqa: E402

out_dir = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
tex_dir = out_dir / "textures"
tex_dir.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
scene.render.fps = 30
arm = bpy.data.objects[spec.ARM_NAME]
mesh = bpy.data.objects[spec.MESH_NAME]
pose_lib.reset(arm)  # rest pose, all IK/FootRot influences 0

textures = []
for img in bpy.data.images:
    if img.source != "FILE" or img.size[0] <= 0 or not img.name.split("_")[0] in ("Color", "NormalGL", "ORM"):
        continue
    clean = re.sub(r"[^A-Za-z0-9_-]+", "_", img.name.split("_")[0])
    target = tex_dir / f"ForestSplitter_{clean}.png"
    copy = img.copy()
    copy.filepath_raw = str(target)
    copy.file_format = "PNG"
    copy.save()
    bpy.data.images.remove(copy)
    img.filepath_raw = str(target)  # FBX references the written file; master stays packed (not saved)
    textures.append(str(target))

bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
fbx = out_dir / "ForestSplitter_Rig.fbx"
bpy.ops.export_scene.fbx(
    filepath=str(fbx), use_selection=True, object_types={"ARMATURE", "MESH"},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
    axis_forward="-Z", axis_up="Y", use_mesh_modifiers=True, mesh_smooth_type="FACE",
    add_leaf_bones=False, use_armature_deform_only=True, armature_nodetype="NULL",
    bake_anim=False, path_mode="RELATIVE", embed_textures=False,
)
report = {"fbx": str(fbx), "textures": textures, "fps": scene.render.fps,
          "fbx_axes": {"forward": "-Z", "up": "Y"}, "deform_only": True, "leaf_bones": False}
(out_dir / "export_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("EXPORTED", json.dumps(report), flush=True)
