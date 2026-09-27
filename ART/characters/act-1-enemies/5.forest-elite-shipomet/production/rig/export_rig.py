"""Validate the rig blend, save textures, export the rest-pose FBX, re-import it, write rig_report.json.

usage: blender -b -P export_rig.py -- <rig.blend> <out.fbx> <build_stats.json> <pose_stretch.json> <report.json>
"""
import json
import re
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import rig_common as rc  # noqa: E402
import rig_validate as rv  # noqa: E402

blend, fbx, stats_p, stretch_p, report_p = [Path(a).resolve() for a in sys.argv[sys.argv.index("--") + 1:]]
bpy.ops.wm.open_mainfile(filepath=str(blend))
arm, mesh = rc.find_rig()
for pb in arm.pose.bones:
    pb.matrix_basis.identity()
arm.animation_data_clear()

tex_dir = blend.parent / "textures"
tex_dir.mkdir(exist_ok=True)
textures = []
for im in bpy.data.images:
    if im.source != "FILE" or im.size[0] == 0:
        continue
    kind = re.split(r"[_ ]", im.name)[0]
    target = tex_dir / f"ForestThorncaster_{kind}.png"
    im.file_format = "PNG"
    im.save(filepath=str(target))
    assert target.exists(), target
    im.filepath = "//textures/" + target.name
    textures.append(str(target))

report = json.loads(stats_p.read_text(encoding="utf-8"))
report["files"] = {"blend": str(blend), "fbx": str(fbx), "textures": textures}
report["armature_object"] = {"name": arm.name, "location": list(arm.location), "rotation": list(arm.rotation_euler),
                             "scale": list(arm.scale)}
report["mesh_object"] = {"name": mesh.name, "triangles": rc.tri_count(mesh), "materials": [m.name for m in mesh.data.materials],
                         "parent": mesh.parent.name, "modifiers": [m.type for m in mesh.modifiers]}
report["bounds"] = rv.mesh_bounds(mesh)
report["weights"] = rv.weight_stats(mesh)
report["bones"] = rv.bone_table(arm)
report["bone_count"] = len(report["bones"])
report["triangle_budget"] = 25000
report["within_budget"] = report["mesh_object"]["triangles"] <= 25000
report["front_axis"] = "-Y (Blender); FBX axis_forward=-Z, axis_up=Y -> Unity +Z forward"
report["unity_humanoid_mapping"] = {
    "Hips": "Hips", "Spine": "Spine02", "Chest": "Spine01", "UpperChest": "Spine", "Neck": "neck", "Head": "Head",
    "LeftShoulder": "LeftShoulder", "LeftUpperArm": "LeftArm", "LeftLowerArm": "LeftForeArm", "LeftHand": "LeftHand",
    "RightShoulder": "RightShoulder", "RightUpperArm": "RightArm", "RightLowerArm": "RightForeArm", "RightHand": "RightHand",
    "LeftUpperLeg": "LeftUpLeg", "LeftLowerLeg": "LeftLeg", "LeftFoot": "LeftFoot", "LeftToes": "LeftToeBase",
    "RightUpperLeg": "RightUpLeg", "RightLowerLeg": "RightLeg", "RightFoot": "RightFoot", "RightToes": "RightToeBase",
    "_note": "Meshy numbers the spine top-down (Spine02 is lowest). If Unity auto-maps 'Spine' to Spine, set it by hand as above. "
             "head_end/headfront are unweighted socket bones; rest pose is an A-pose (use Enforce T-Pose).",
}
report["deformation_test"] = {"poses": json.loads(stretch_p.read_text(encoding="utf-8")),
                              "renders": str(blend.parent / "deform_test")}
bpy.context.scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(blend))

bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=str(fbx), use_selection=True, object_types={"ARMATURE", "MESH"},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
    axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False, mesh_smooth_type="FACE",
    add_leaf_bones=False, use_armature_deform_only=True, armature_nodetype="NULL",
    bake_anim=False, path_mode="RELATIVE", embed_textures=False)

# round trip
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
rarm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
rmesh = next(o for o in bpy.data.objects if o.type == "MESH")
bpy.context.view_layer.update()
rb = rv.mesh_bounds(rmesh)
head = rarm.matrix_world @ rarm.data.bones["Head"].head_local
front = rarm.matrix_world @ rarm.data.bones["headfront"].head_local
report["fbx_roundtrip"] = {
    "bones": len(rarm.data.bones), "bone_names_match": sorted(b.name for b in rarm.data.bones) == sorted(b["name"] for b in report["bones"]),
    "triangles": rc.tri_count(rmesh), "vertex_groups": len(rmesh.vertex_groups),
    "height_m": rb["height_m"], "min_z": rb["min"][2], "armature_world_scale": [round(s, 4) for s in rarm.matrix_world.to_scale()],
    "faces_minus_y": bool(front.y < head.y), "unweighted_vertices": rv.weight_stats(rmesh)["unweighted_vertices"],
}
report_p.write_text(json.dumps(report, indent=1), encoding="utf-8")
print("EXPORT_OK", json.dumps(report["fbx_roundtrip"]))
