"""Inspect the Корнехват rig for animation: objects, contact vertex sets, rest clearances.
blender -b <rig.blend> -P inspect_rig.py -- <out.json>
"""
import sys, json
from pathlib import Path
import bpy

out = Path(sys.argv[sys.argv.index("--") + 1])
sc = bpy.context.scene
arm = bpy.data.objects["ARM_ForestRootSnarer"]
mesh = bpy.data.objects["SM_ForestRootSnarer_LOD0"]
info = {"objects": {o.name: {"type": o.type, "parent": o.parent.name if o.parent else None,
                             "mods": [(m.type, getattr(m, "object", None) and m.object.name) for m in o.modifiers],
                             "matrix_world_is_identity": o.matrix_world == o.matrix_world.Identity(4)}
                    for o in sc.objects},
        "actions": [a.name for a in bpy.data.actions], "fps": sc.render.fps,
        "unit_scale": sc.unit_settings.scale_length,
        "constraints": {pb.name: [(c.type, c.name, c.influence) for c in pb.constraints] for pb in arm.pose.bones if pb.constraints}}
gi = {g.name: g.index for g in mesh.vertex_groups}


def weight(v, name):
    for g in v.groups:
        if g.group == gi.get(name):
            return g.weight
    return 0.0


sets = {}
for s in ("L", "R"):
    for key, bone, thr in (("hand", f"{s}_arm_lower", 0.99), ("foot", f"{s}_foot", 0.6), ("shin", f"{s}_leg_lower", 0.6)):
        ids = [v.index for v in mesh.data.vertices if weight(v, bone) >= thr]
        cos = [mesh.data.vertices[i].co for i in ids]
        low = sorted(cos, key=lambda c: c.z)[:15]
        sets[f"{s}_{key}"] = {"bone": bone, "count": len(ids), "min_z": round(min(c.z for c in cos), 4) if cos else None,
                              "lowest_centroid": [round(sum(c[k] for c in low) / len(low), 3) for k in range(3)] if low else None,
                              "bbox_min": [round(min(c[k] for c in cos), 3) for k in range(3)] if cos else None,
                              "bbox_max": [round(max(c[k] for c in cos), 3) for k in range(3)] if cos else None}
info["contact_sets"] = sets
body = [v.co for v in mesh.data.vertices
        if max((weight(v, b) for b in ("pelvis", "spine_01", "spine_02", "neck", "head", "jaw")), default=0) > 0.5]
info["body_min_z"] = round(min(c.z for c in body), 4)
low = sorted(body, key=lambda c: c.z)[:40]
info["body_lowest_centroid"] = [round(sum(c[k] for c in low) / len(low), 3) for k in range(3)]
# belly profile: lowest body vertex per 10 cm band along Y (|x|<0.3)
prof = {}
for c in body:
    if abs(c.x) < 0.3:
        k = round(c.y, 1)
        prof[k] = min(prof.get(k, 9), c.z)
info["belly_profile_y_to_minz"] = {str(k): round(v, 3) for k, v in sorted(prof.items())}
info["bones_len"] = {b.name: round(b.length, 4) for b in arm.data.bones}
out.write_text(json.dumps(info, indent=1), encoding="utf-8")
print("INSPECT_DONE")
