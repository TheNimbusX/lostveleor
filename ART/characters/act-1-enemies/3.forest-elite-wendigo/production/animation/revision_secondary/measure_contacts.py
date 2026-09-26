import json
import sys
from pathlib import Path
import bpy

out = Path(sys.argv[sys.argv.index("--") + 1])
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH" and o.find_armature() == rig)
groups = {}
for side in ("L", "R"):
    for part in ("foot", "hand"):
        names = [f"{side}_{part}"]
        if part == "foot":
            names.append(f"{side}_toe")
        gids = {mesh.vertex_groups[n].index for n in names}
        threshold = 0.45 if part == "hand" else 0.60
        groups[f"{side}_{part}"] = [v.index for v in mesh.data.vertices
                                    if sum(g.weight for g in v.groups if g.group in gids) >= threshold]
data = {}
for clip in ("Walk", "Death"):
    action = bpy.data.actions[f"AN_ForestWendigo_{clip}"]
    end = int(action.frame_range[1])
    rig.animation_data.action = action
    data[clip] = []
    for f in range(end+1):
        scene.frame_set(f)
        bpy.context.view_layer.update()
        dep = bpy.context.evaluated_depsgraph_get()
        obj = mesh.evaluated_get(dep)
        m = obj.to_mesh()
        zs = [(obj.matrix_world @ v.co).z for v in m.vertices]
        record = {"frame":f, "mesh_min": round(min(zs),4),
                  "below_3cm":sum(z < -.03 for z in zs)}
        for key, ids in groups.items():
            record[f"{key}_min"] = round(min(zs[i] for i in ids),4)
            b = rig.pose.bones[key]
            record[f"{key}_head_z"] = round((rig.matrix_world @ b.head).z,4)
            record[f"{key}_head_y"] = round((rig.matrix_world @ b.head).y,4)
        data[clip].append(record)
        obj.to_mesh_clear()
out.write_text(json.dumps(data,indent=2))
for key in data:
    print(key, data[key][::4])
