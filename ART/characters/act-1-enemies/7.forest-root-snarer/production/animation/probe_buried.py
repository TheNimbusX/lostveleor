"""Which vertex groups are under the ground in the Slam hold (non-slab vertices below z = -1 cm)?
blender -b ForestRootSnarer_Anim_r01.blend -P probe_buried.py"""
import sys, json
from collections import Counter
from pathlib import Path
import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_core  # noqa: E402

arm = bpy.data.objects[anim_core.ARM_NAME]
mesh = bpy.data.objects[anim_core.MESH_NAME]
rig = anim_core.Rig(arm, mesh)
gi = {g.index: g.name for g in mesh.vertex_groups}
dom = np.array([gi[max(v.groups, key=lambda g: g.weight).group] for v in mesh.data.vertices])
w_lower = np.array([sum(g.weight for g in v.groups if gi[g.group].endswith("arm_lower")) for v in mesh.data.vertices])
arm.animation_data.action = bpy.data.actions["ForestRootSnarer_Slam"]
res = {}
for f in range(0, 73):
    bpy.context.scene.frame_set(f)
    co = rig.mesh_co()
    low = (co[:, 2] < -0.01) & ~np.char.endswith(dom.astype(str), "arm_lower")
    res[f] = {"count": int(low.sum()), "groups": dict(Counter(dom[low]).most_common(5)),
              "min_z": round(float(co[low][:, 2].min()), 3) if low.any() else None,
              "arm_lower_weight_of_those": [round(float(w_lower[low].min()), 2), round(float(w_lower[low].max()), 2)] if low.any() else None,
              "top_of_buried_z": round(float(co[low][:, 2].max()), 3) if low.any() else None}
bad = {f: r for f, r in res.items() if r["count"]}
print("BURIED", json.dumps(bad), flush=True)
