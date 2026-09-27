"""Re-import the FBX into an empty scene and check skeleton, skin and orientation.
blender -b --factory-startup -P verify_fbx.py -- <fbx> <out_json>
"""
import sys, json, math
from pathlib import Path
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
fbx, rep = Path(argv[0]).resolve(), Path(argv[1]).resolve()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH")
dg = bpy.context.evaluated_depsgraph_get()


def world_co():
    ev = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    return [mesh.matrix_world @ v.co for v in ev.data.vertices]


co = world_co()
lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
gl = mesh.vertex_groups["L_arm_lower"].index
lslab = [co[v.index] for v in mesh.data.vertices if any(g.group == gl and g.weight > 0.999 for g in v.groups)]
lslab_c = sum(lslab, Vector()) / max(1, len(lslab))
mesh.data.calc_loop_triangles()

# rigidity check on the re-imported skin: rotate L_arm_lower 60 deg and compare slab vertex distances
gi = mesh.vertex_groups["L_arm_lower"].index
slab = [v.index for v in mesh.data.vertices if any(g.group == gi and g.weight > 0.999 for g in v.groups)]
pb = arm.pose.bones["L_arm_lower"]
pb.rotation_mode = "XYZ"
pb.rotation_euler = (math.radians(60), 0, 0)
bpy.context.view_layer.update()
co2 = world_co()
pairs = [(slab[i], slab[i + 7]) for i in range(0, len(slab) - 7, 7)]
dev = max(abs((co2[a] - co2[b]).length - (co[a] - co[b]).length) for a, b in pairs) if pairs else None
moved = max((co2[i] - co[i]).length for i in slab) if slab else 0

out = {
    "fbx": str(fbx),
    "bones": [b.name for b in arm.data.bones],
    "bone_count": len(arm.data.bones),
    "vertex_groups": len(mesh.vertex_groups),
    "triangles": len(mesh.data.loop_triangles),
    "bbox_min": [round(c, 3) for c in lo], "bbox_max": [round(c, 3) for c in hi],
    "L_slab_centroid": [round(c, 3) for c in lslab_c],
    "faces_minus_y_and_L_is_plus_x": lslab_c.y < -0.2 and lslab_c.x > 0.3,
    "armature_modifier": any(m.type == "ARMATURE" and m.object == arm for m in mesh.modifiers),
    "slab_rigid_vertices": len(slab),
    "slab_rigid_max_distance_change_m": round(dev, 6) if dev is not None else None,
    "slab_max_move_m": round(moved, 3),
    "materials": [m.name for m in mesh.data.materials if m],
    "images": [(i.name, i.filepath, list(i.size)) for i in bpy.data.images],
}
rep.write_text(json.dumps(out, indent=2), encoding="utf-8")
print("VERIFY_DONE", json.dumps(out), flush=True)
