"""Print what the rig blend holds (objects, constraints, bone axes, feet heights, render setup)."""
import json

import bpy

s = bpy.context.scene
out = {"objects": [], "fps": s.render.fps, "engine": s.render.engine, "camera": s.camera.name if s.camera else None,
       "world": s.world.name if s.world else None, "actions": [a.name for a in bpy.data.actions]}
for o in bpy.data.objects:
    out["objects"].append([o.name, o.type, [round(v, 3) for v in o.location], o.parent.name if o.parent else None,
                           [m.type for m in getattr(o, "modifiers", [])]])
arm = bpy.data.objects["ARM_ForestSplitter"]
out["constraints"] = {pb.name: [(c.type, c.name, c.influence) for c in pb.constraints] for pb in arm.pose.bones if pb.constraints}
out["axes"] = {}
for b in arm.data.bones:
    m = b.matrix_local.to_3x3()
    out["axes"][b.name] = {"x": [round(v, 3) for v in m.col[0]], "y": [round(v, 3) for v in m.col[1]], "z": [round(v, 3) for v in m.col[2]]}
mesh = bpy.data.objects["SM_ForestSplitter_LOD0"]
vs = mesh.data.vertices
out["mesh_bounds"] = [[round(min(v.co[i] for v in vs), 3) for i in range(3)], [round(max(v.co[i] for v in vs), 3) for i in range(3)]]
out["mesh_parent"] = mesh.parent.name if mesh.parent else None
out["mesh_matrix_identity"] = [list(r) for r in mesh.matrix_world]
# lowest verts per foot group
feet = {}
for g in mesh.vertex_groups:
    if g.name.endswith("_foot"):
        zs = [v.co.z for v in vs if any(e.group == g.index and e.weight > 0.5 for e in v.groups)]
        feet[g.name] = [round(min(zs), 4), len(zs)] if zs else None
out["feet_min_z"] = feet
print("INSPECT_JSON " + json.dumps(out))
