"""Export an editable Wendigo master and a Unity-oriented FBX/GLB skeleton."""

import json
import re
import sys
from pathlib import Path

import bpy

source = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
out_dir = Path(sys.argv[sys.argv.index("--") + 2]).resolve()
out_dir.mkdir(parents=True, exist_ok=True)
textures_dir = out_dir / "textures"
textures_dir.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source))
scene = bpy.context.scene
mesh = next(obj for obj in scene.objects if obj.type == "MESH")
armature = next(obj for obj in scene.objects if obj.type == "ARMATURE")
scene.render.fps = 30

saved_images = []
for image in bpy.data.images:
    if image.source != "FILE" or image.size[0] <= 0 or image.size[1] <= 0:
        continue
    clean = re.sub(r"[^A-Za-z0-9_-]+", "_", image.name).strip("_")
    target = textures_dir / (clean + ".png")
    image.filepath_raw = str(target)
    image.file_format = "PNG"
    image.save()
    saved_images.append(str(target))

master = out_dir / "ForestWendigo_Rig.blend"
bpy.ops.wm.save_as_mainfile(filepath=str(master))

bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
armature.select_set(True)
bpy.context.view_layer.objects.active = armature
fbx = out_dir / "ForestWendigo_Rig.fbx"
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
    bake_anim=False,
    path_mode="COPY",
    embed_textures=False,
)

glb = out_dir / "ForestWendigo_Rig.glb"
bpy.ops.export_scene.gltf(
    filepath=str(glb),
    export_format="GLB",
    use_selection=True,
    export_apply=True,
    export_animations=False,
    export_skins=True,
    export_def_bones=True,
)

report = {
    "master_blend": str(master),
    "unity_fbx": str(fbx),
    "inspection_glb": str(glb),
    "textures": saved_images,
    "fps": scene.render.fps,
    "source_blend": str(source),
}
(out_dir / "export_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report), flush=True)
