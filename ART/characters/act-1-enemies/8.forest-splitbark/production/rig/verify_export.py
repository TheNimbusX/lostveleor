"""Round-trip check of ForestSplitter_Rig.fbx against the master .blend (read-only).

usage: blender -b ForestSplitter_Rig.blend -P verify_export.py -- <rig_dir>
Compares bone heads, triangle count, per-vertex weights and a posed shell between master and FBX.
"""
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Quaternion, Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import rig_spec as spec  # noqa: E402

rig_dir = Path(sys.argv[sys.argv.index("--") + 1]).resolve()


def snapshot(arm, mesh):
    names = [g.name for g in mesh.vertex_groups]
    deform = [b.name for b in arm.data.bones if b.use_deform]
    W = np.zeros((len(mesh.data.vertices), len(deform)))
    col = {n: i for i, n in enumerate(deform)}
    for v in mesh.data.vertices:
        for g in v.groups:
            if names[g.group] in col:
                W[v.index, col[names[g.group]]] = g.weight
    co = np.array([mesh.matrix_world @ v.co for v in mesh.data.vertices])
    mesh.data.calc_loop_triangles()
    heads = {b.name: list(arm.matrix_world @ b.head_local) for b in arm.data.bones if b.use_deform}
    return {"deform": deform, "W": W, "co": co, "tris": len(mesh.data.loop_triangles), "heads": heads}


def posed(arm, mesh, bone, deg):
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = (1, 0, 0, 0)
    pb = arm.pose.bones[bone]
    rest = (arm.matrix_world.to_quaternion() @ pb.bone.matrix_local.to_quaternion())
    q = Quaternion(Vector((0, 1, 0)), math.radians(deg))
    pb.rotation_quaternion = rest.inverted() @ q @ rest
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.array([mesh.matrix_world @ v.co for v in me.vertices])
    ev.to_mesh_clear()
    pb.rotation_quaternion = (1, 0, 0, 0)
    return co


src_arm, src_mesh = bpy.data.objects[spec.ARM_NAME], bpy.data.objects[spec.MESH_NAME]
for pb in src_arm.pose.bones:
    for c in pb.constraints:
        c.influence = 0.0
src = snapshot(src_arm, src_mesh)
src_pose = posed(src_arm, src_mesh, "shell_L", 20)

# Clear the scene by hand. NOT read_factory_settings: it disables user extensions and makes Blender
# delete their extracted wheels in extensions/.local (shared with other running Blender sessions).
for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.images):
    for block in list(coll):
        coll.remove(block)
bpy.ops.import_scene.fbx(filepath=str(rig_dir / "ForestSplitter_Rig.fbx"))
arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
arm, mesh = arms[0], meshes[0]
dst = snapshot(arm, mesh)
dst_pose = posed(arm, mesh, "shell_L", 20)

same_order = dst["co"].shape == src["co"].shape and np.abs(dst["co"] - src["co"]).max() < 1e-3
cols = [dst["deform"].index(n) for n in src["deform"]] if set(dst["deform"]) == set(src["deform"]) else None
report = {
    "fbx_bytes": (rig_dir / "ForestSplitter_Rig.fbx").stat().st_size,
    "objects": {"armatures": len(arms), "meshes": len(meshes), "mesh_name": mesh.name, "armature_name": arm.name},
    "bones_fbx": len(dst["deform"]), "bones_master_deform": len(src["deform"]),
    "bone_names_match": set(dst["deform"]) == set(src["deform"]),
    "max_bone_head_error_m": round(max((Vector(dst["heads"][n]) - Vector(h)).length for n, h in src["heads"].items()), 6),
    "triangles_fbx": dst["tris"], "triangles_master": src["tris"],
    "vertex_order_preserved": bool(same_order),
    "bounds_min": [round(float(x), 4) for x in dst["co"].min(0)],
    "bounds_max": [round(float(x), 4) for x in dst["co"].max(0)],
    "max_influences": int((dst["W"] > 1e-6).sum(1).max()),
    "unweighted_vertices": int(((dst["W"] > 1e-6).sum(1) == 0).sum()),
    "max_weight_sum_error": round(float(np.abs(dst["W"].sum(1) - 1).max()), 5),
}
if same_order and cols is not None:
    report["max_weight_diff_vs_master"] = round(float(np.abs(dst["W"][:, cols] - src["W"]).max()), 5)
    report["shell_L_20deg_max_vertex_diff_m"] = round(float(np.linalg.norm(dst_pose - src_pose, axis=1).max()), 6)
    report["shell_L_20deg_moved_vertices"] = int((np.linalg.norm(dst_pose - dst["co"], axis=1) > 1e-4).sum())
(rig_dir / "roundtrip_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("ROUNDTRIP", json.dumps(report), flush=True)
